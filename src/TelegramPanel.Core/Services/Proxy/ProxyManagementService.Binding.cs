using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Models;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

public sealed partial class ProxyManagementService
{
    internal async ValueTask<MutationLease> AcquireMutationLeaseAsync(
        CancellationToken cancellationToken = default)
    {
        await MutationLock.WaitAsync(cancellationToken);
        return new MutationLease(this);
    }

    /// <summary>
    /// 解除账号的代理绑定，并回收不再使用的账号专属代理资源。
    /// </summary>
    public async Task ReleaseAccountBindingAsync(
        int accountId,
        int? expectedProxyId = null,
        CancellationToken cancellationToken = default)
    {
        if (accountId <= 0)
            return;

        await using var lease = await AcquireMutationLeaseAsync(cancellationToken);
        await ReleaseAccountBindingWithinMutationAsync(
            lease,
            accountId,
            expectedProxyId,
            deactivateAccount: false,
            cancellationToken);
    }

    /// <summary>
    /// 在调用方已持有代理变更锁时解除绑定。锁租约会阻止误用和重复加锁。
    /// </summary>
    internal async Task ReleaseAccountBindingWithinMutationAsync(
        MutationLease lease,
        int accountId,
        int? expectedProxyId,
        bool deactivateAccount,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lease);
        lease.EnsureHeldBy(this);
        if (accountId <= 0)
            return;

        var account = await _db.Accounts
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken);
        if (account == null)
        {
            await ReleaseClientsStrictAsync(new[] { accountId });
            return;
        }

        var currentProxyId = account.ProxyId ?? 0;
        if (expectedProxyId.HasValue && currentProxyId != expectedProxyId.Value)
            throw new ProxyBindingConflictException("账号代理绑定已变化，未执行删除清理");

        if (deactivateAccount && account.IsActive)
        {
            account.IsActive = false;
            await _db.SaveChangesAsync(cancellationToken);
        }

        // 只有确认旧客户端已断开后，才允许解除绑定或回收独立代理资源。
        await ReleaseClientsStrictAsync(new[] { accountId });

        OutboundProxy? oldProxy = null;
        if (account.ProxyId.HasValue)
        {
            oldProxy = await _db.OutboundProxies
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == account.ProxyId.Value, cancellationToken);

            if (oldProxy?.Kind == OutboundProxyKinds.Resin)
            {
                await ReleaseResinLeaseAsync(oldProxy, accountId, cancellationToken);
            }

            account.ProxyId = null;
            account.Proxy = null;
        }

        if (oldProxy != null)
            await _db.SaveChangesAsync(cancellationToken);

        if (oldProxy?.Kind != OutboundProxyKinds.Warp)
            return;

        try
        {
            await CleanupReplacedWarpProxiesAsync(
                new[] { oldProxy.Id },
                targetProxyId: null,
                cancellationToken);
        }
        catch (Exception cleanupError)
        {
            var message = deactivateAccount
                ? "WARP 资源清理失败，账号已解除代理绑定并停用，可重试删除旧 WARP"
                : "WARP 资源清理失败，账号已解除旧代理绑定，可重试删除旧 WARP";
            throw new InvalidOperationException(message, cleanupError);
        }
    }

    public async Task<AccountProxyBatchResult> BindAccountsAsync(
        IReadOnlyCollection<int> accountIds,
        AccountProxyBindingInput input,
        CancellationToken cancellationToken = default)
    {
        var ids = accountIds.Where(x => x > 0).Distinct().ToArray();
        if (ids.Length == 0)
            throw new ArgumentException("请选择账号");
        if (ids.Length > 500)
            throw new ArgumentException("单次最多处理 500 个账号");

        var strategy = NormalizeStrategy(input.Strategy);
        if (strategy == "global")
            _ = RequireGlobalProxy();
        if (strategy == "warp_per_account" && ids.Length > 10)
            throw new ArgumentException("逐账号创建 WARP 单次最多处理 10 个账号");

        if (strategy == "warp_per_account")
        {
            var warpResults = new List<AccountProxyOperationResult>(ids.Length);
            foreach (var accountId in ids)
            {
                try
                {
                    warpResults.Add(await BindOneWarpAsync(
                        accountId,
                        ids.Length == 1 ? input.ExpectedProxyId : null,
                        cancellationToken));
                }
                catch (Exception ex)
                {
                    var phone = await _db.Accounts.AsNoTracking()
                        .Where(x => x.Id == accountId)
                        .Select(x => x.Phone)
                        .FirstOrDefaultAsync(cancellationToken);
                    warpResults.Add(new AccountProxyOperationResult(
                        accountId,
                        phone,
                        false,
                        "WARP 创建或绑定失败",
                        SafeError(ex)));
                }
            }

            return ToBatchResult(warpResults);
        }

        var targetProxyId = strategy is "direct" or "global"
            ? null
            : input.ProxyId is > 0
                ? input.ProxyId
                : throw new ArgumentException("请选择已有代理");

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            OutboundProxy? targetProxy = null;
            if (targetProxyId.HasValue)
            {
                targetProxy = await _db.OutboundProxies
                    .FirstOrDefaultAsync(
                        x => x.Id == targetProxyId.Value && x.IsEnabled,
                        cancellationToken)
                    ?? throw new KeyNotFoundException("所选代理不存在或已停用");
            }

            var accounts = await _db.Accounts
                .Include(x => x.Proxy)
                .Where(x => ids.Contains(x.Id))
                .ToListAsync(cancellationToken);
            var found = accounts.Select(x => x.Id).ToHashSet();
            var results = new List<AccountProxyOperationResult>(ids.Length);

            if (ids.Length == 1 && input.ExpectedProxyId.HasValue)
            {
                var account = accounts.FirstOrDefault();
                var current = account?.ProxyId ?? 0;
                if (current != input.ExpectedProxyId.Value)
                    throw new ProxyBindingConflictException("账号代理绑定已变化，请刷新后重试");
            }

            var oldBindings = accounts
                .Where(x => x.ProxyId.HasValue && x.Proxy != null)
                .Select(x => new PreviousProxyBinding(x.Id, x.Proxy!))
                .ToList();

            var activationStates = accounts.ToDictionary(x => x.Id, x => x.IsActive);
            foreach (var account in accounts)
                account.IsActive = false;
            if (activationStates.Values.Any(x => x))
                await _db.SaveChangesAsync(cancellationToken);

            // 提交新路由前必须确认旧客户端已经断开。否则数据库虽然显示已切换，
            // 内存中的已登录客户端仍可能继续使用旧出口，造成“伪切换”。
            await ReleaseClientsStrictAsync(accounts.Select(x => x.Id));

            var resinBindings = oldBindings
                .Where(x => x.Proxy.Kind == OutboundProxyKinds.Resin
                            && x.Proxy.Id != targetProxyId)
                .ToList();
            foreach (var old in resinBindings)
                await ReleaseResinLeaseAsync(old.Proxy, old.AccountId, cancellationToken);

            var useGlobalProxy = strategy == "global";
            foreach (var account in accounts)
            {
                account.ProxyId = targetProxyId;
                account.UseGlobalProxy = useGlobalProxy;
                account.IsActive = activationStates[account.Id];
            }
            if (targetProxy != null && targetProxy.FirstBoundAtUtc == null)
                targetProxy.FirstBoundAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            foreach (var account in accounts)
            {
                results.Add(new AccountProxyOperationResult(
                    account.Id,
                    account.Phone,
                    true,
                    targetProxy == null
                        ? useGlobalProxy ? "已切换为全局代理设置" : "已切换为直连"
                        : $"已绑定代理 {targetProxy.Name}",
                    null,
                    targetProxyId));
            }
            foreach (var missing in ids.Where(x => !found.Contains(x)))
            {
                results.Add(new AccountProxyOperationResult(
                    missing,
                    null,
                    false,
                    "账号不存在",
                    "账号不存在"));
            }

            try
            {
                await CleanupReplacedWarpProxiesAsync(
                    oldBindings.Select(x => x.Proxy.Id).Distinct(),
                    targetProxyId,
                    cancellationToken);
            }
            catch (Exception cleanupError)
            {
                // 新路由已经提交，不能再把账号指回已停用或已部分删除的旧 WARP。
                throw new InvalidOperationException(
                    "代理切换已提交，但旧 WARP 资源清理失败，请重试删除旧 WARP",
                    cleanupError);
            }

            return ToBatchResult(results);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task ValidateBindingInputAsync(
        AccountProxyBindingInput input,
        CancellationToken cancellationToken = default)
    {
        var strategy = NormalizeStrategy(input.Strategy);
        if (strategy == "global")
        {
            _ = RequireGlobalProxy();
        }
        else if (strategy == "existing")
        {
            if (input.ProxyId is not > 0)
                throw new ArgumentException("请选择已有代理");
            var exists = await _db.OutboundProxies
                .AsNoTracking()
                .AnyAsync(x => x.Id == input.ProxyId && x.IsEnabled, cancellationToken);
            if (!exists)
                throw new KeyNotFoundException("所选代理不存在或已停用");
        }
        else if (strategy == "warp_per_account")
        {
            var status = await _warpManager.GetStatusAsync(cancellationToken);
            if (!status.PlatformSupported || !status.Enabled || !status.DockerAvailable)
                throw new InvalidOperationException(status.Error ?? "WARP 运行条件不可用");
        }
    }

    private async Task<AccountProxyOperationResult> BindOneWarpAsync(
        int accountId,
        int? expectedProxyId,
        CancellationToken cancellationToken)
    {
        var accountSnapshot = await _db.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new KeyNotFoundException("账号不存在");
        var currentProxyId = accountSnapshot.ProxyId ?? 0;
        if (expectedProxyId.HasValue && currentProxyId != expectedProxyId.Value)
            throw new ProxyBindingConflictException("账号代理绑定已变化，未创建 WARP");

        var newProxy = await _warpManager.CreateAsync(
            $"WARP · {accountSnapshot.DisplayPhone}",
            $"account-{accountId}-{Guid.NewGuid():N}",
            cancellationToken);
        var keep = false;
        try
        {
            var result = await BindAccountsAsync(
                new[] { accountId },
                new AccountProxyBindingInput("existing", newProxy.Id, currentProxyId),
                cancellationToken);
            var item = result.Items.Single();
            keep = item.Success;
            return item with
            {
                Summary = $"已创建并绑定独立 WARP {newProxy.Name}",
                ProxyId = newProxy.Id
            };
        }
        finally
        {
            if (!keep)
            {
                try
                {
                    await DeleteAsync(newProxy.Id, CancellationToken.None);
                }
                catch (Exception cleanupError)
                {
                    _logger.LogWarning(
                        cleanupError,
                        "Failed to compensate unbound WARP proxy {ProxyId}",
                        newProxy.Id);
                }
            }
        }
    }

    private async Task CleanupReplacedWarpProxiesAsync(
        IEnumerable<int> oldProxyIds,
        int? targetProxyId,
        CancellationToken cancellationToken)
    {
        foreach (var oldId in oldProxyIds.Where(x => x != targetProxyId).Distinct())
        {
            var oldProxy = await _db.OutboundProxies
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == oldId, cancellationToken);
            if (oldProxy?.Kind != OutboundProxyKinds.Warp)
                continue;

            var stillUsed = await _db.Accounts
                .AnyAsync(x => x.ProxyId == oldId, cancellationToken);
            if (stillUsed)
                continue;

            await DeleteUnusedProxyCoreAsync(oldProxy, cancellationToken);
        }
    }

    private async Task ReleaseClientsAsync(IEnumerable<int> accountIds)
    {
        foreach (var accountId in accountIds.Distinct())
        {
            try
            {
                await _clientPool.RemoveClientAsync(accountId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release Telegram client {AccountId}", accountId);
            }
        }
    }

    private async Task ReleaseClientsStrictAsync(IEnumerable<int> accountIds)
    {
        foreach (var accountId in accountIds.Distinct())
            await _clientPool.RemoveClientStrictAsync(accountId);
    }

    private static AccountProxyBatchResult ToBatchResult(
        IReadOnlyList<AccountProxyOperationResult> items) =>
        new(
            items.Count(x => x.Success),
            items.Count(x => !x.Success),
            items);

    private sealed record PreviousProxyBinding(
        int AccountId,
        OutboundProxy Proxy);

    internal sealed class MutationLease : IAsyncDisposable
    {
        private ProxyManagementService? _owner;

        internal MutationLease(ProxyManagementService owner)
        {
            _owner = owner;
        }

        internal void EnsureHeldBy(ProxyManagementService owner)
        {
            if (!ReferenceEquals(Volatile.Read(ref _owner), owner))
                throw new InvalidOperationException("代理变更锁租约无效或已释放");
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _owner, null) != null)
                MutationLock.Release();
            return ValueTask.CompletedTask;
        }
    }
}
