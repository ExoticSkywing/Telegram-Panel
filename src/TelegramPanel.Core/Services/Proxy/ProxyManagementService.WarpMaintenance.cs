using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Services.Proxy;

public sealed partial class ProxyManagementService
{
    public async Task<IReadOnlyList<int>> ListEnabledWarpProxyIdsAsync(
        CancellationToken cancellationToken = default)
    {
        return await _db.OutboundProxies
            .AsNoTracking()
            .Where(x => x.Kind == OutboundProxyKinds.Warp
                && x.IsEnabled
                && x.WarpProfile != null
                && x.WarpProfile.DesiredEnabled
                && x.WarpProfile.Status != "deleted"
                && x.WarpProfile.Status != "deleting"
                && x.WarpProfile.Status != "cleanup_pending")
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// 探测一个受管 WARP，并在连续失败、到达可选定时刷新周期，或用户明确要求时
    /// 重启容器。整个过程始终保留原代理绑定，不允许降级为面板直连。
    /// </summary>
    public async Task<WarpMaintenanceResult> MaintainWarpAsync(
        int proxyId,
        WarpMaintenanceOptions options,
        bool forceRestart = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        await MutationLock.WaitAsync(cancellationToken);
        try
        {
            var proxy = await _db.OutboundProxies
                .Include(x => x.Accounts)
                .Include(x => x.WarpProfile)
                .FirstOrDefaultAsync(x => x.Id == proxyId, cancellationToken)
                ?? throw new KeyNotFoundException("代理不存在");
            if (proxy.Kind != OutboundProxyKinds.Warp || proxy.WarpProfile == null)
                throw new InvalidOperationException("该代理不是受管 WARP");

            var profile = proxy.WarpProfile;
            if (_temporaryWarpClaims?.OwnsRequest(profile.RequestId) == true)
                return ProtectedLoginResult(proxy, forceRestart);

            using var maintenanceLease = _warpProxyUsageGuard?.TryAcquireMaintenance(proxy.Id);
            if (_warpProxyUsageGuard != null && maintenanceLease == null)
                return ProtectedLoginResult(proxy, forceRestart);
            if (!proxy.IsEnabled || !profile.DesiredEnabled)
            {
                return FailureResult(
                    proxy,
                    restarted: false,
                    "WARP 已停用，未执行巡检或刷新",
                    "WARP 已停用");
            }
            if (profile.Status is "deleting" or "deleted" or "cleanup_pending" or "failed")
            {
                return FailureResult(
                    proxy,
                    restarted: false,
                    $"WARP 当前状态为 {profile.Status}，不能自动恢复",
                    profile.LastError ?? "WARP 资源状态异常");
            }

            var now = DateTime.UtcNow;
            var scheduledRefreshDue = options.ScheduledRefreshEnabled
                && now >= (profile.LastRecoveryAttemptAtUtc ?? profile.CreatedAtUtc)
                    .AddMinutes(options.ScheduledRefreshIntervalMinutes);

            if (!forceRestart)
            {
                await TestAsyncCore(proxy, cancellationToken);
                if (proxy.TestStatus == "ok" && !scheduledRefreshDue)
                {
                    return new WarpMaintenanceResult(
                        proxy.Id,
                        proxy.Name,
                        true,
                        false,
                        false,
                        profile.Status,
                        $"WARP 出口正常：{proxy.EgressIp ?? "未知 IP"}",
                        null);
                }

                if (proxy.TestStatus != "ok"
                    && profile.ConsecutiveFailures < options.FailureThreshold)
                {
                    return FailureResult(
                        proxy,
                        restarted: false,
                        $"WARP 连续检测失败 {profile.ConsecutiveFailures}/{options.FailureThreshold}，尚未达到自动恢复阈值",
                        proxy.LastError ?? "WARP 出口检测失败");
                }

                if (proxy.TestStatus != "ok"
                    && profile.LastRecoveryAttemptAtUtc.HasValue
                    && now < profile.LastRecoveryAttemptAtUtc.Value
                        .AddMinutes(options.RecoveryCooldownMinutes))
                {
                    return FailureResult(
                        proxy,
                        restarted: false,
                        $"WARP 仍在恢复冷却期，暂不重复重启",
                        proxy.LastError ?? "WARP 出口检测失败");
                }
            }

            var reason = forceRestart
                ? "手动刷新"
                : scheduledRefreshDue && proxy.TestStatus == "ok"
                    ? "定时刷新"
                    : "故障自动恢复";
            return await RestartWarpCoreAsync(
                proxy,
                options,
                reason,
                cancellationToken);
        }
        finally
        {
            MutationLock.Release();
        }
    }

    public async Task<WarpMaintenanceBatchResult> MaintainAllWarpAsync(
        WarpMaintenanceOptions options,
        bool forceRestart,
        CancellationToken cancellationToken = default)
    {
        var proxyIds = await ListEnabledWarpProxyIdsAsync(cancellationToken);
        var results = new List<WarpMaintenanceResult>(proxyIds.Count);
        foreach (var proxyId in proxyIds)
        {
            try
            {
                results.Add(await MaintainWarpAsync(
                    proxyId,
                    options,
                    forceRestart,
                    cancellationToken));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                results.Add(new WarpMaintenanceResult(
                    proxyId,
                    $"WARP #{proxyId}",
                    false,
                    false,
                    false,
                    "error",
                    "WARP 维护失败",
                    SafeError(ex)));
            }
        }

        return ToWarpMaintenanceBatchResult(results);
    }

    private async Task<WarpMaintenanceResult> RestartWarpCoreAsync(
        TelegramPanel.Data.Entities.OutboundProxy proxy,
        WarpMaintenanceOptions options,
        string reason,
        CancellationToken cancellationToken)
    {
        var profile = proxy.WarpProfile
            ?? throw new InvalidOperationException("WARP 运行配置缺失");
        var now = DateTime.UtcNow;
        profile.Status = "restarting";
        profile.LastRecoveryAttemptAtUtc = now;
        profile.LastError = null;
        profile.UpdatedAtUtc = now;
        // 数据库只允许 unknown/ok/fail；运行中的恢复态由 WarpProfile.Status 表示。
        proxy.TestStatus = "unknown";
        proxy.LastError = null;
        proxy.UpdatedAtUtc = now;
        await _db.SaveChangesAsync(cancellationToken);

        var accountIds = proxy.Accounts.Select(x => x.Id).ToList();
        if (IsEnabledGlobalProxy(proxy.Id))
        {
            var globalAccountIds = await _db.Accounts
                .AsNoTracking()
                .Where(x => x.ProxyId == null && x.UseGlobalProxy)
                .Select(x => x.Id)
                .ToListAsync(cancellationToken);
            accountIds.AddRange(globalAccountIds);
            accountIds = accountIds.Distinct().ToList();
        }
        await ReleaseClientsAsync(accountIds);
        try
        {
            await _warpManager.RestartContainerAsync(profile, cancellationToken);

            for (var attempt = 1; attempt <= options.RecoveryProbeAttempts; attempt++)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(options.RecoveryProbeDelaySeconds),
                    cancellationToken);
                await TestAsyncCore(proxy, cancellationToken);
                if (proxy.TestStatus == "ok")
                    break;
            }

            if (proxy.TestStatus != "ok")
            {
                throw new InvalidOperationException(
                    proxy.LastError ?? $"WARP 重启后连续 {options.RecoveryProbeAttempts} 次出口检测失败");
            }

            var recoveredAt = DateTime.UtcNow;
            profile.Status = "active";
            profile.ConsecutiveFailures = 0;
            profile.LastRecoveredAtUtc = recoveredAt;
            profile.RecoveryCount++;
            profile.LastError = null;
            profile.UpdatedAtUtc = recoveredAt;
            proxy.LastError = null;
            proxy.UpdatedAtUtc = recoveredAt;
            await ReleaseClientsAsync(accountIds);
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Managed WARP proxy {ProxyId} recovered by {Reason}; egress {EgressIp}",
                proxy.Id,
                reason,
                proxy.EgressIp);
            return new WarpMaintenanceResult(
                proxy.Id,
                proxy.Name,
                true,
                true,
                true,
                profile.Status,
                $"{reason}完成，当前出口 {proxy.EgressIp ?? "未知 IP"}",
                null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var error = SafeError(ex);
            var failedAt = DateTime.UtcNow;
            profile.Status = error.Contains("容器不存在", StringComparison.Ordinal)
                ? "missing"
                : "degraded";
            profile.LastError = error;
            profile.UpdatedAtUtc = failedAt;
            proxy.TestStatus = "fail";
            proxy.LastError = error;
            proxy.UpdatedAtUtc = failedAt;
            await _db.SaveChangesAsync(CancellationToken.None);
            _logger.LogWarning(
                ex,
                "Managed WARP proxy {ProxyId} failed recovery by {Reason}",
                proxy.Id,
                reason);
            return FailureResult(
                proxy,
                restarted: true,
                $"{reason}失败",
                error);
        }
    }

    private static WarpMaintenanceResult FailureResult(
        TelegramPanel.Data.Entities.OutboundProxy proxy,
        bool restarted,
        string summary,
        string error) =>
        new(
            proxy.Id,
            proxy.Name,
            false,
            restarted,
            false,
            proxy.WarpProfile?.Status ?? "missing",
            summary,
            error);

    private static WarpMaintenanceResult ProtectedLoginResult(
        TelegramPanel.Data.Entities.OutboundProxy proxy,
        bool forceRestart) =>
        forceRestart
            ? FailureResult(
                proxy,
                restarted: false,
                "WARP 正被账号登录或导入流程使用，已阻止刷新",
                "首次连接出口仍处于冻结会话中")
            : new WarpMaintenanceResult(
                proxy.Id,
                proxy.Name,
                true,
                false,
                false,
                proxy.WarpProfile?.Status ?? "missing",
                "WARP 正被首次连接流程使用，本轮保持冻结出口并跳过巡检",
                null);

    private static WarpMaintenanceBatchResult ToWarpMaintenanceBatchResult(
        IReadOnlyList<WarpMaintenanceResult> items) =>
        new(
            items.Count,
            items.Count(x => x.Success && !x.Recovered),
            items.Count(x => x.Recovered),
            items.Count(x => !x.Success),
            items);
}
