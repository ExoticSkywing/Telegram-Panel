using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

public sealed class ProxyInUseException : InvalidOperationException
{
    public ProxyInUseException(string message) : base(message)
    {
    }
}

public sealed class ProxyBindingConflictException : InvalidOperationException
{
    public ProxyBindingConflictException(string message) : base(message)
    {
    }
}

/// <summary>
/// 统一管理普通代理、Resin 网关、WARP 资源和账号绑定。
/// </summary>
public sealed partial class ProxyManagementService
{
    private static readonly SemaphoreSlim MutationLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly ITelegramClientPool _clientPool;
    private readonly ProxyEgressProbeService _probeService;
    private readonly WarpContainerManager _warpManager;
    private readonly ILogger<ProxyManagementService> _logger;
    private readonly IConfiguration? _configuration;

    public ProxyManagementService(
        AppDbContext db,
        ITelegramClientPool clientPool,
        ProxyEgressProbeService probeService,
        WarpContainerManager warpManager,
        ILogger<ProxyManagementService> logger,
        IConfiguration? configuration = null)
    {
        _db = db;
        _clientPool = clientPool;
        _probeService = probeService;
        _warpManager = warpManager;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<IReadOnlyList<OutboundProxy>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.OutboundProxies
            .AsNoTracking()
            .Include(x => x.Accounts)
            .Include(x => x.WarpProfile)
            .OrderByDescending(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<OutboundProxy?> GetAsync(
        int id,
        bool includeAccounts = false,
        CancellationToken cancellationToken = default)
    {
        IQueryable<OutboundProxy> query = _db.OutboundProxies;
        if (includeAccounts)
            query = query.Include(x => x.Accounts);
        return await query
            .Include(x => x.WarpProfile)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<OutboundProxy> CreateAsync(
        OutboundProxyInput input,
        CancellationToken cancellationToken = default)
    {
        var normalized = NormalizeInput(input, existing: null);
        if (normalized.Kind == OutboundProxyKinds.Warp)
            throw new ArgumentException("受管 WARP 请使用一键创建入口");

        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            await EnsureNoDuplicateAsync(normalized, exceptId: null, cancellationToken);
            var now = DateTime.UtcNow;
            var proxy = new OutboundProxy
            {
                Name = normalized.Name!,
                Kind = normalized.Kind!,
                Protocol = normalized.Protocol!,
                Host = normalized.Host!,
                Port = normalized.Port,
                Username = normalized.Username,
                Password = normalized.Password,
                Secret = normalized.Secret,
                ResinPlatform = normalized.ResinPlatform,
                ResinAdminUrl = normalized.ResinAdminUrl,
                ResinAdminToken = normalized.ResinAdminToken,
                IsEnabled = normalized.IsEnabled,
                TestStatus = "unknown",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            _db.OutboundProxies.Add(proxy);
            await _db.SaveChangesAsync(cancellationToken);

            return normalized.TestAfterSave
                ? await TestAsyncCore(proxy, cancellationToken)
                : proxy;
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task<OutboundProxy> UpdateAsync(
        int id,
        OutboundProxyInput input,
        CancellationToken cancellationToken = default)
    {
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var proxy = await _db.OutboundProxies
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("代理不存在");
            var boundAccounts = await _db.Accounts
                .Where(x => x.ProxyId == proxy.Id)
                .ToListAsync(cancellationToken);

            if (proxy.Kind == OutboundProxyKinds.Warp)
            {
                var warpProfile = proxy.WarpProfile
                    ?? throw new InvalidOperationException("WARP 运行配置缺失，请删除后重新创建");
                var enabledChanged = proxy.IsEnabled != input.IsEnabled;
                var lifecycleChangeRequired = enabledChanged
                    || warpProfile.DesiredEnabled != input.IsEnabled
                    || (input.IsEnabled
                        ? warpProfile.Status != "active"
                        : warpProfile.Status != "stopped");
                IReadOnlyDictionary<int, bool>? warpActivationStates = null;
                if (lifecycleChangeRequired)
                {
                    warpActivationStates = await QuiesceBoundAccountsAsync(
                        boundAccounts,
                        cancellationToken);
                    await ReleaseClientsStrictAsync(boundAccounts.Select(x => x.Id));
                    await _warpManager.SetContainerEnabledAsync(
                        warpProfile,
                        input.IsEnabled,
                        cancellationToken);
                }

                var now = DateTime.UtcNow;
                proxy.Name = NormalizeName(input.Name, proxy.Name);
                proxy.IsEnabled = input.IsEnabled;
                warpProfile.DesiredEnabled = input.IsEnabled;
                if (lifecycleChangeRequired)
                {
                    warpProfile.Status = input.IsEnabled ? "active" : "stopped";
                    warpProfile.LastError = null;
                    warpProfile.UpdatedAtUtc = now;
                    ResetProbeState(proxy);
                }
                if (lifecycleChangeRequired && input.IsEnabled)
                    RestoreBoundAccountActivation(boundAccounts, warpActivationStates!);
                proxy.UpdatedAtUtc = now;
                await _db.SaveChangesAsync(cancellationToken);
                return input.TestAfterSave
                    ? await TestAsyncCore(proxy, cancellationToken)
                    : proxy;
            }

            var normalized = NormalizeInput(input, proxy);
            if (normalized.Kind == OutboundProxyKinds.Warp)
                throw new ArgumentException("受管 WARP 请使用一键创建入口");
            await EnsureNoDuplicateAsync(normalized, id, cancellationToken);
            var connectionChanged =
                !string.Equals(proxy.Kind, normalized.Kind, StringComparison.Ordinal)
                || !string.Equals(proxy.Protocol, normalized.Protocol, StringComparison.Ordinal)
                || !string.Equals(proxy.Host, normalized.Host, StringComparison.OrdinalIgnoreCase)
                || proxy.Port != normalized.Port
                || !string.Equals(proxy.Username, normalized.Username, StringComparison.Ordinal)
                || !string.Equals(proxy.Password, normalized.Password, StringComparison.Ordinal)
                || !string.Equals(proxy.Secret, normalized.Secret, StringComparison.Ordinal)
                || !string.Equals(
                    proxy.ResinPlatform,
                    normalized.ResinPlatform,
                    StringComparison.Ordinal)
                || !string.Equals(
                    proxy.ResinAdminUrl,
                    normalized.ResinAdminUrl,
                    StringComparison.Ordinal)
                || !string.Equals(proxy.ResinAdminToken, normalized.ResinAdminToken, StringComparison.Ordinal)
                || proxy.IsEnabled != normalized.IsEnabled;

            var resinIdentityChanged = proxy.Kind == OutboundProxyKinds.Resin
                && (normalized.Kind != OutboundProxyKinds.Resin
                    || !string.Equals(
                        proxy.ResinPlatform ?? "Default",
                        normalized.ResinPlatform ?? "Default",
                        StringComparison.Ordinal)
                    || !string.Equals(
                        proxy.ResinAdminUrl,
                        normalized.ResinAdminUrl,
                        StringComparison.Ordinal)
                    || !string.Equals(
                        proxy.ResinAdminToken,
                        normalized.ResinAdminToken,
                        StringComparison.Ordinal));

            IReadOnlyDictionary<int, bool>? activationStates = null;
            if (connectionChanged)
            {
                activationStates = await QuiesceBoundAccountsAsync(
                    boundAccounts,
                    cancellationToken);
                await ReleaseClientsStrictAsync(boundAccounts.Select(x => x.Id));
            }

            if (resinIdentityChanged)
            {
                // 清理失败时必须保留旧控制面凭据，否则旧 Lease 将永久失去回收入口。
                var accountIds = boundAccounts.Select(x => x.Id).ToArray();
                if (!connectionChanged)
                    await ReleaseClientsStrictAsync(accountIds);
                foreach (var accountId in accountIds)
                    await ReleaseResinLeaseAsync(proxy, accountId, cancellationToken);
            }

            proxy.Name = normalized.Name!;
            proxy.Kind = normalized.Kind!;
            proxy.Protocol = normalized.Protocol!;
            proxy.Host = normalized.Host!;
            proxy.Port = normalized.Port;
            proxy.Username = normalized.Username;
            if (input.ClearPassword || !string.IsNullOrWhiteSpace(input.Password))
                proxy.Password = normalized.Password;
            proxy.Secret = normalized.Secret;
            proxy.ResinPlatform = normalized.ResinPlatform;
            proxy.ResinAdminUrl = normalized.ResinAdminUrl;
            proxy.ResinAdminToken = normalized.ResinAdminToken;
            proxy.IsEnabled = normalized.IsEnabled;
            if (connectionChanged)
                ResetProbeState(proxy);
            if (connectionChanged && normalized.IsEnabled)
                RestoreBoundAccountActivation(boundAccounts, activationStates!);
            proxy.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return normalized.TestAfterSave
                ? await TestAsyncCore(proxy, cancellationToken)
                : proxy;
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task DeleteAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var proxy = await _db.OutboundProxies
                .Include(x => x.Accounts)
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("代理不存在");

            if (proxy.Accounts.Count > 0)
                throw new ProxyInUseException($"代理仍被 {proxy.Accounts.Count} 个账号使用，请先切换账号代理");

            await DeleteUnusedProxyCoreAsync(proxy, cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task<OutboundProxy> TestAsync(
        int id,
        CancellationToken cancellationToken = default)
    {
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var proxy = await _db.OutboundProxies
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == id, cancellationToken)
                ?? throw new KeyNotFoundException("代理不存在");
            return await TestAsyncCore(proxy, cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public Task<EgressProbeResult> ProbePanelAsync(
        CancellationToken cancellationToken = default) =>
        _probeService.ProbePanelAsync(cancellationToken);

    public async Task<EgressProbeResult> ProbeAccountAsync(
        int accountId,
        CancellationToken cancellationToken = default)
    {
        var account = await _db.Accounts
            .AsNoTracking()
            .Include(x => x.Proxy)
            .FirstOrDefaultAsync(x => x.Id == accountId, cancellationToken)
            ?? throw new KeyNotFoundException("账号不存在");

        if (!account.ProxyId.HasValue)
        {
            if (!account.UseGlobalProxy)
                return await _probeService.ProbePanelAsync(cancellationToken);

            var globalProxy = RequireGlobalProxy();
            return await _probeService.ProbeProxyAsync(
                globalProxy,
                requireWarp: false,
                cancellationToken);
        }
        if (account.Proxy is not { IsEnabled: true } proxy)
            throw new InvalidOperationException("账号绑定的代理不存在或已停用，已阻止检测面板直连出口");

        return await _probeService.ProbeProxyAsync(
            proxy,
            $"tg_account_{account.Id}",
            cancellationToken);
    }

    private ProxyConnectionOptions RequireGlobalProxy()
    {
        if (_configuration == null)
        {
            throw new InvalidOperationException(
                "Telegram 全局代理配置不可用，已阻止降级为直连");
        }

        return GlobalTelegramProxyConfiguration.BuildRequired(_configuration);
    }

    public Task<OutboundProxy> CreateWarpAsync(
        string? name,
        string? requestId,
        CancellationToken cancellationToken = default,
        string? protocol = null) =>
        _warpManager.CreateAsync(name, requestId, cancellationToken, protocol);

    public Task<WarpRuntimeStatus> GetWarpStatusAsync(
        CancellationToken cancellationToken = default) =>
        _warpManager.GetStatusAsync(cancellationToken);

    private static void ResetProbeState(OutboundProxy proxy)
    {
        proxy.TestStatus = "unknown";
        proxy.LastError = null;
        proxy.LastLatencyMs = null;
        proxy.EgressIp = null;
        proxy.EgressCountry = null;
        proxy.EgressCity = null;
        proxy.EgressIsp = null;
        proxy.LastTestedAtUtc = null;
    }

    private async Task<IReadOnlyDictionary<int, bool>> QuiesceBoundAccountsAsync(
        IReadOnlyCollection<Account> accounts,
        CancellationToken cancellationToken)
    {
        var activationStates = accounts.ToDictionary(x => x.Id, x => x.IsActive);
        foreach (var account in accounts)
            account.IsActive = false;

        if (activationStates.Values.Any(x => x))
            await _db.SaveChangesAsync(cancellationToken);

        return activationStates;
    }

    private static void RestoreBoundAccountActivation(
        IEnumerable<Account> accounts,
        IReadOnlyDictionary<int, bool> activationStates)
    {
        foreach (var account in accounts)
            account.IsActive = activationStates[account.Id];
    }

    public async Task<IReadOnlyList<OutboundProxy>> ImportAsync(
        string? text,
        bool testAfterImport,
        CancellationToken cancellationToken = default)
    {
        var lines = (text ?? string.Empty)
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0 && !x.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (lines.Length == 0)
            throw new ArgumentException("请填写代理地址");
        if (lines.Length > 500)
            throw new ArgumentException("单次最多导入 500 条代理");

        // 先解析全部输入，避免格式错误导致前半批已写入、后半批失败。
        var inputs = lines
            .Select(line => ParseImportLine(line, testAfterImport))
            .ToArray();

        var created = new List<OutboundProxy>();
        foreach (var input in inputs)
        {
            try
            {
                created.Add(await CreateAsync(input, cancellationToken));
            }
            catch (InvalidOperationException ex)
                when (ex.Message.Contains("已存在", StringComparison.Ordinal))
            {
                _logger.LogInformation("Skipped duplicate proxy during import");
            }
        }

        return created;
    }
}
