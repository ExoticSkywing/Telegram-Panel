using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 清理用户关闭页面后遗留的临时登录客户端、WARP 和 Resin 登录身份。
/// </summary>
public sealed class AccountLoginProxyCleanupService : BackgroundService
{
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    private readonly AccountLoginProxyStateStore _store;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TemporaryWarpClaimStore _temporaryWarpClaims;
    private readonly ILogger<AccountLoginProxyCleanupService> _logger;

    public AccountLoginProxyCleanupService(
        AccountLoginProxyStateStore store,
        IServiceScopeFactory scopeFactory,
        TemporaryWarpClaimStore temporaryWarpClaims,
        ILogger<AccountLoginProxyCleanupService> logger)
    {
        _store = store;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _temporaryWarpClaims = temporaryWarpClaims;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(SweepInterval);
        try
        {
            await SweepBestEffortAsync(stoppingToken);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await SweepBestEffortAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 正常停止。
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await base.StopAsync(cancellationToken);
        }
        finally
        {
            using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (_store.TryTakeAny(out var state) && state != null)
            {
                if (await CleanupClaimedStateBestEffortAsync(state, cleanupTimeout.Token))
                    continue;
                break;
            }
        }
    }

    private async Task SweepBestEffortAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 清理失败不能终止宿主；保留数据库恢复入口，下个周期继续重试。
            _logger.LogError(ex, "Login proxy cleanup sweep failed and will be retried");
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTimeOffset.UtcNow - SessionLifetime;
        while (_store.TryTakeExpired(cutoff, out var state) && state != null)
        {
            if (await CleanupClaimedStateBestEffortAsync(state, cancellationToken))
                continue;
            break;
        }

        await CleanupRestartOrphansAsync(cutoff, cancellationToken);
    }

    private async Task<bool> CleanupClaimedStateBestEffortAsync(
        AccountLoginProxyState state,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var coordinator = scope.ServiceProvider.GetRequiredService<AccountLoginProxyCoordinator>();
            await coordinator.ReleaseClaimedStateAsync(state, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to strictly stop login {LoginId}; frozen proxy state will be retried",
                state.LoginId);
            return false;
        }
    }

    private async Task CleanupRestartOrphansAsync(
        DateTimeOffset cutoffUtc,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var proxyManagement = scope.ServiceProvider
            .GetRequiredService<ProxyManagementService>();
        var proxies = await proxyManagement.ListAsync(cancellationToken);
        var globalProxyId = proxyManagement.GetEnabledGlobalProxyId();
        var orphanIds = proxies
            .Where(proxy => IsRestartOrphan(
                proxy,
                cutoffUtc,
                _store,
                _temporaryWarpClaims,
                globalProxyId))
            .Select(proxy => proxy.Id)
            .ToArray();

        foreach (var proxyId in orphanIds)
        {
            using var cleanupLease = _store.TryAcquireMaintenance(proxyId);
            if (cleanupLease == null)
                continue;

            try
            {
                // 候选列表与实际删除之间可能出现新的首次连接或导入占用。
                // 维护租约阻止新的 WARP 使用者进入，再复查请求声明和正式账号绑定。
                var candidate = await proxyManagement.GetAsync(
                    proxyId,
                    includeAccounts: true,
                    cancellationToken);
                if (candidate == null
                    || !IsRestartOrphan(
                        candidate,
                        cutoffUtc,
                        _store,
                        _temporaryWarpClaims,
                        globalProxyId))
                {
                    continue;
                }

                await proxyManagement.DeleteAsync(proxyId, cancellationToken);
                _logger.LogInformation(
                    "Cleaned orphaned temporary WARP proxy {ProxyId}",
                    proxyId);
            }
            catch (KeyNotFoundException)
            {
                // 已被并发清理。
            }
            catch (ProxyInUseException)
            {
                // 登录完成并发绑定成功，由正式账号继续持有。
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to clean orphaned temporary WARP proxy {ProxyId}",
                    proxyId);
            }
        }
    }

    internal static bool IsRestartOrphan(
        OutboundProxy proxy,
        DateTimeOffset cutoffUtc,
        AccountLoginProxyStateStore store,
        TemporaryWarpClaimStore temporaryWarpClaims,
        int? globalProxyId = null)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(temporaryWarpClaims);
        return proxy.Kind == OutboundProxyKinds.Warp
               && proxy.Id != globalProxyId
               && proxy.Accounts.Count == 0
               && proxy.WarpProfile?.RequestId is { } requestId
               && (AccountLoginProxyCoordinator.IsManagedWarpRequestId(requestId)
                   || AccountImportService.IsManagedWarpRequestId(requestId))
               && proxy.WarpProfile.CreatedAtUtc <= cutoffUtc.UtcDateTime
               && !store.OwnsWarpProxy(proxy.Id)
               && !temporaryWarpClaims.OwnsRequest(requestId);
    }
}
