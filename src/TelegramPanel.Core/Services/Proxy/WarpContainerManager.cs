using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.Models;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;

namespace TelegramPanel.Core.Services.Proxy;

/// <summary>
/// 通过 Docker Engine API 创建并管理 Cloudflare WARP 容器。
/// </summary>
public sealed class WarpContainerManager
{
    private const string OwnerLabel = "com.telegram-panel.warp";
    private const string ProfileLabel = "com.telegram-panel.warp.profile";
    private static readonly SemaphoreSlim LifecycleLock = new(1, 1);

    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ProxyEgressProbeService _probeService;
    private readonly ILogger<WarpContainerManager> _logger;
    private readonly IWarpDockerClientFactory _dockerFactory;

    public WarpContainerManager(
        AppDbContext db,
        IConfiguration configuration,
        ProxyEgressProbeService probeService,
        ILogger<WarpContainerManager> logger)
        : this(
            db,
            configuration,
            probeService,
            logger,
            DockerEngineClientFactory.Instance)
    {
    }

    internal WarpContainerManager(
        AppDbContext db,
        IConfiguration configuration,
        ProxyEgressProbeService probeService,
        ILogger<WarpContainerManager> logger,
        IWarpDockerClientFactory dockerFactory)
    {
        _db = db;
        _configuration = configuration;
        _probeService = probeService;
        _logger = logger;
        _dockerFactory = dockerFactory;
    }

    public async Task<WarpRuntimeStatus> GetStatusAsync(
        CancellationToken cancellationToken = default)
    {
        var settings = WarpSettings.From(_configuration);
        var platformSupported = _dockerFactory.PlatformSupported;
        if (!platformSupported)
        {
            return new WarpRuntimeStatus(
                false,
                settings.Enabled,
                false,
                null,
                "WARP 仅支持在 Linux Docker 环境中运行",
                settings.Image,
                settings.Network,
                settings.ProxyHostMode,
                settings.Protocol);
        }

        if (!settings.Enabled)
        {
            return new WarpRuntimeStatus(
                true,
                false,
                false,
                null,
                "WARP 未启用，请设置 Proxy:Warp:Enabled=true",
                settings.Image,
                settings.Network,
                settings.ProxyHostMode,
                settings.Protocol);
        }

        try
        {
            using var docker = _dockerFactory.Create(settings.DockerSocketPath);
            var version = await docker.GetVersionAsync(cancellationToken);
            return new WarpRuntimeStatus(
                true,
                true,
                true,
                version,
                null,
                settings.Image,
                settings.Network,
                settings.ProxyHostMode,
                settings.Protocol);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WarpRuntimeStatus(
                true,
                true,
                false,
                null,
                SafeError(ex),
                settings.Image,
                settings.Network,
                settings.ProxyHostMode,
                settings.Protocol);
        }
    }

    public async Task<OutboundProxy> CreateAsync(
        string? displayName = null,
        string? requestId = null,
        CancellationToken cancellationToken = default,
        string? protocol = null)
    {
        displayName = NormalizeDisplayName(displayName);
        protocol = NormalizeRequestedProtocol(protocol);
        await LifecycleLock.WaitAsync(cancellationToken);
        try
        {
            requestId = NormalizeRequestId(requestId);
            WarpSettings? settings = null;
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                var existing = await _db.WarpProfiles
                    .Include(x => x.Proxy)
                    .FirstOrDefaultAsync(x => x.RequestId == requestId, cancellationToken);
                if (existing?.Proxy is { IsEnabled: true } existingProxy
                    && existing.Status == "active")
                {
                    if (protocol != null
                        && !string.Equals(existingProxy.Protocol, protocol, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"该 WARP 请求已按 {existingProxy.Protocol.ToUpperInvariant()} 协议创建，不能改为 {protocol.ToUpperInvariant()}");
                    }

                    return existingProxy;
                }
                if (existing != null)
                {
                    if (existing.Proxy == null && existing.Status is "failed" or "cleanup_pending")
                    {
                        settings = WarpSettings.From(_configuration);
                        await CleanupHistoricalFailedProfileAsync(
                            existing,
                            settings,
                            cancellationToken);
                    }
                    else if (existing.Proxy == null && existing.Status == "deleted")
                    {
                        existing.RequestId = null;
                        existing.UpdatedAtUtc = DateTime.UtcNow;
                        await _db.SaveChangesAsync(cancellationToken);
                    }
                    else
                    {
                        throw new InvalidOperationException("该 WARP 请求已存在且无法复用，请更换请求 ID");
                    }
                }
            }

            var status = await GetStatusAsync(cancellationToken);
            if (!status.PlatformSupported || !status.Enabled || !status.DockerAvailable)
                throw new InvalidOperationException(status.Error ?? "Docker WARP 服务不可用");

            settings ??= WarpSettings.From(_configuration);
            settings = settings with { Protocol = protocol ?? settings.Protocol };
            using var operationTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationTimeout.CancelAfter(TimeSpan.FromMinutes(12));
            var token = operationTimeout.Token;

            var profileKey = Guid.NewGuid().ToString("N");
            var shortKey = profileKey[..12];
            var profileId = $"telegram-panel-warp-{profileKey}";
            var containerName = $"{settings.ContainerPrefix}-{shortKey}";
            var volumeName = $"{settings.ContainerPrefix}-data-{shortKey}";
            var hostPort = await AllocateHostPortAsync(
                settings.HostPortStart,
                settings.ProxyHostMode == "published",
                token);
            var now = DateTime.UtcNow;
            var proxyHost = settings.ProxyHostMode == "container"
                ? containerName
                : settings.ProxyHost;
            var proxyPort = settings.ProxyHostMode == "container"
                ? settings.ContainerPort
                : hostPort;
            var createdProxy = new OutboundProxy
            {
                Name = displayName ?? $"WARP {shortKey}",
                Kind = OutboundProxyKinds.Warp,
                Protocol = settings.Protocol,
                Host = proxyHost,
                Port = proxyPort,
                IsEnabled = false,
                TestStatus = "unknown",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            var profile = new WarpProfile
            {
                ProfileId = profileId,
                RequestId = requestId,
                ContainerName = containerName,
                VolumeName = volumeName,
                HostPort = hostPort,
                Status = "creating",
                DesiredEnabled = false,
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
                Proxy = createdProxy
            };
            _db.OutboundProxies.Add(createdProxy);
            _db.WarpProfiles.Add(profile);
            IWarpDockerClient? docker = null;

            try
            {
                // 恢复入口必须先于任何 Docker 资源落地，并与 Profile 同次提交。
                await _db.SaveChangesAsync(token);
                docker = _dockerFactory.Create(settings.DockerSocketPath);
                await docker.GetVersionAsync(token);
                await docker.EnsureImageAsync(settings.Image, settings.PullIfMissing, token);
                await docker.CreateVolumeAsync(volumeName, profileId, token);

                var containerId = await docker.CreateContainerAsync(
                    settings,
                    profileId,
                    containerName,
                    volumeName,
                    hostPort,
                    token);
                profile.ContainerId = containerId;
                profile.Status = "starting";
                profile.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(token);

                await docker.StartContainerAsync(containerId, token);

                EgressProbeResult? probe = null;
                for (var attempt = 1; attempt <= 35; attempt++)
                {
                    await Task.Delay(attempt <= 5 ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(4), token);
                    probe = await _probeService.ProbeProxyAsync(
                        createdProxy,
                        $"warp_probe_{shortKey}",
                        token);
                    if (probe.Success)
                        break;

                    profile.LastError = probe.Error;
                    profile.Status = "starting";
                    profile.UpdatedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(token);
                }

                if (probe is not { Success: true })
                    throw new InvalidOperationException(probe?.Error ?? "WARP 代理未通过出口检测");

                createdProxy.IsEnabled = true;
                createdProxy.TestStatus = "ok";
                createdProxy.LastError = null;
                createdProxy.LastLatencyMs = probe.LatencyMs;
                createdProxy.EgressIp = probe.Ip;
                createdProxy.EgressCountry = probe.Country;
                createdProxy.EgressCity = probe.City;
                createdProxy.EgressIsp = probe.Isp;
                createdProxy.LastTestedAtUtc = probe.CheckedAtUtc;
                createdProxy.UpdatedAtUtc = DateTime.UtcNow;
                profile.Status = "active";
                profile.DesiredEnabled = true;
                profile.EgressIp = probe.Ip;
                profile.Country = probe.Country;
                profile.WarpStatus = probe.WarpStatus;
                profile.LastError = null;
                profile.LastCheckedAtUtc = probe.CheckedAtUtc;
                profile.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(token);
            }
            catch (Exception creationError)
            {
                MarkCreationFailed(profile, createdProxy, creationError);
                using var compensationTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var compensationToken = compensationTimeout.Token;
                try
                {
                    await _db.SaveChangesAsync(compensationToken);
                }
                catch (Exception stateError)
                {
                    throw new InvalidOperationException(
                        "WARP 创建失败，且无法持久化可恢复状态；Docker 资源未继续清理",
                        new AggregateException(creationError, stateError));
                }

                try
                {
                    if (docker != null)
                    {
                        await RemoveDockerResourcesStrictAsync(
                            docker,
                            profile,
                            purgeData: true,
                            compensationToken);
                    }
                }
                catch (Exception cleanupError)
                {
                    var cleanupMessage = $"{SafeError(creationError)}；资源清理失败：{SafeError(cleanupError)}";
                    profile.LastError = cleanupMessage;
                    profile.UpdatedAtUtc = DateTime.UtcNow;
                    createdProxy.LastError = cleanupMessage;
                    createdProxy.UpdatedAtUtc = DateTime.UtcNow;
                    await TrySaveAsync(compensationToken);
                    throw new InvalidOperationException(
                        "WARP 创建失败且资源清理失败，已保留禁用代理供删除重试",
                        new AggregateException(creationError, cleanupError));
                }

                // 只有 Docker 资源严格清理完成后，才删除恢复入口并释放请求幂等键。
                _db.WarpProfiles.Remove(profile);
                _db.OutboundProxies.Remove(createdProxy);
                await _db.SaveChangesAsync(compensationToken);
                throw;
            }
            finally
            {
                docker?.Dispose();
            }

            _logger.LogInformation(
                "Created managed WARP profile {ProfileId} with proxy {ProxyId}",
                profile.ProfileId,
                createdProxy.Id);
            return createdProxy;
        }
        finally
        {
            LifecycleLock.Release();
        }
    }

    public async Task DeleteResourcesAsync(
        WarpProfile profile,
        bool purgeData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await LifecycleLock.WaitAsync(cancellationToken);
        try
        {
            var settings = WarpSettings.From(_configuration);
            using var docker = _dockerFactory.Create(settings.DockerSocketPath);
            await docker.GetVersionAsync(cancellationToken);
            await RemoveDockerResourcesStrictAsync(
                docker,
                profile,
                purgeData,
                cancellationToken);

            profile.Status = purgeData ? "deleted" : "stopped";
            profile.DesiredEnabled = false;
            profile.ContainerId = null;
            profile.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            LifecycleLock.Release();
        }
    }

    /// <summary>
    /// 严格启动或停止受管 WARP 容器。仅在容器归属校验通过后执行，
    /// 不修改数据库状态，由调用方在 Docker 操作成功后统一提交。
    /// </summary>
    public async Task SetContainerEnabledAsync(
        WarpProfile profile,
        bool enabled,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await LifecycleLock.WaitAsync(cancellationToken);
        try
        {
            if (profile.Status is "creating" or "starting" or "deleting"
                or "deleted" or "failed" or "cleanup_pending")
            {
                throw new InvalidOperationException(
                    $"WARP 当前状态为 {profile.Status}，不能执行{(enabled ? "启动" : "停止")}");
            }

            var settings = WarpSettings.From(_configuration);
            if (!_dockerFactory.PlatformSupported)
                throw new InvalidOperationException("WARP 仅支持在 Linux Docker 环境中运行");
            if (enabled && !settings.Enabled)
                throw new InvalidOperationException("WARP 未启用，请设置 Proxy:Warp:Enabled=true");

            var containerReference = string.IsNullOrWhiteSpace(profile.ContainerId)
                ? profile.ContainerName
                : profile.ContainerId;
            if (string.IsNullOrWhiteSpace(containerReference))
                throw new InvalidOperationException("WARP 容器标识缺失，请删除后重新创建");

            using var docker = _dockerFactory.Create(settings.DockerSocketPath);
            await docker.GetVersionAsync(cancellationToken);
            if (!await docker.VerifyContainerOwnershipAsync(
                    containerReference,
                    profile.ProfileId,
                    cancellationToken))
            {
                throw new InvalidOperationException("WARP 容器不存在，请删除后重新创建");
            }

            if (enabled)
                await docker.StartContainerAsync(containerReference, cancellationToken);
            else
                await docker.StopContainerAsync(containerReference, cancellationToken);
        }
        finally
        {
            LifecycleLock.Release();
        }
    }

    private async Task CleanupHistoricalFailedProfileAsync(
        WarpProfile profile,
        WarpSettings settings,
        CancellationToken cancellationToken)
    {
        using var docker = _dockerFactory.Create(settings.DockerSocketPath);
        await docker.GetVersionAsync(cancellationToken);
        await RemoveDockerResourcesStrictAsync(
            docker,
            profile,
            purgeData: true,
            cancellationToken);

        // 删除成功才释放 RequestId；数据库提交失败时，同一请求仍无法创建新资源。
        _db.WarpProfiles.Remove(profile);
        await _db.SaveChangesAsync(cancellationToken);
    }

    private static async Task RemoveDockerResourcesStrictAsync(
        IWarpDockerClient docker,
        WarpProfile profile,
        bool purgeData,
        CancellationToken cancellationToken)
    {
        var errors = new List<Exception>();
        var containerReference = string.IsNullOrWhiteSpace(profile.ContainerId)
            ? profile.ContainerName
            : profile.ContainerId;

        try
        {
            if (!string.IsNullOrWhiteSpace(containerReference)
                && await docker.VerifyContainerOwnershipAsync(
                    containerReference,
                    profile.ProfileId,
                    cancellationToken))
            {
                await docker.RemoveContainerAsync(containerReference, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            errors.Add(ex);
        }

        if (purgeData && !string.IsNullOrWhiteSpace(profile.VolumeName))
        {
            try
            {
                if (await docker.VerifyVolumeOwnershipAsync(
                    profile.VolumeName,
                    profile.ProfileId,
                    cancellationToken))
                {
                    await docker.RemoveVolumeAsync(profile.VolumeName, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex);
            }
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                $"WARP 资源清理失败：{string.Join("；", errors.Select(SafeError))}",
                errors.Count == 1 ? errors[0] : new AggregateException(errors));
        }
    }

    private static void MarkCreationFailed(
        WarpProfile profile,
        OutboundProxy proxy,
        Exception exception)
    {
        var now = DateTime.UtcNow;
        var error = SafeError(exception);
        // cleanup_pending 必须继续占用 HostPort，避免补偿失败时复用仍被容器占用的 published 端口。
        profile.Status = "cleanup_pending";
        profile.DesiredEnabled = false;
        profile.LastError = error;
        profile.UpdatedAtUtc = now;
        proxy.IsEnabled = false;
        proxy.TestStatus = "fail";
        proxy.LastError = error;
        proxy.UpdatedAtUtc = now;
    }

    private async Task<int> AllocateHostPortAsync(
        int start,
        bool requireHostPort,
        CancellationToken cancellationToken)
    {
        var used = await _db.WarpProfiles
            .Where(x => x.Status != "deleted" && x.Status != "failed")
            .Select(x => x.HostPort)
            .ToListAsync(cancellationToken);
        var usedSet = used.ToHashSet();
        for (var port = start; port <= 65535; port++)
        {
            if (!usedSet.Contains(port)
                && (!requireHostPort || IsHostPortAvailable(port)))
                return port;
        }

        throw new InvalidOperationException("没有可用的 WARP 端口");
    }

    private static bool IsHostPortAvailable(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private async Task TrySaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist WARP failure state");
        }
    }

    private static string? NormalizeRequestId(string? requestId)
    {
        requestId = string.IsNullOrWhiteSpace(requestId) ? null : requestId.Trim();
        if (requestId is { Length: > 128 }
            || requestId?.Any(ch => !char.IsLetterOrDigit(ch) && ch is not '-' and not '_' and not '.') == true)
        {
            throw new ArgumentException("WARP 请求 ID 无效", nameof(requestId));
        }

        return requestId;
    }

    private static string? NormalizeDisplayName(string? displayName)
    {
        displayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim();
        if (displayName is { Length: > 200 })
            throw new ArgumentException("WARP 显示名不能超过 200 个字符", nameof(displayName));

        return displayName;
    }

    private static string? NormalizeRequestedProtocol(string? protocol)
    {
        if (string.IsNullOrWhiteSpace(protocol))
            return null;

        var normalized = protocol.Trim().ToLowerInvariant();
        if (normalized is not (OutboundProxyProtocols.Http or OutboundProxyProtocols.Socks5))
        {
            throw new ArgumentException(
                "WARP 代理协议仅支持 HTTP 或 SOCKS5",
                nameof(protocol));
        }

        return normalized;
    }

    private static string SafeError(Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return message.Length <= 1000 ? message : message[..1000];
    }

    internal sealed record WarpSettings(
        bool Enabled,
        string DockerSocketPath,
        string Image,
        string Network,
        string ContainerPrefix,
        string Protocol,
        int ContainerPort,
        int HostPortStart,
        string ProxyHostMode,
        string ProxyHost,
        bool PullIfMissing)
    {
        public static WarpSettings From(IConfiguration configuration)
        {
            var mode = Read(configuration, "Proxy:Warp:ProxyHostMode", "container").ToLowerInvariant();
            if (mode is not ("container" or "published"))
                mode = "container";

            var protocol = Read(configuration, "Proxy:Warp:Protocol", "http").ToLowerInvariant();
            if (protocol is not (OutboundProxyProtocols.Http or OutboundProxyProtocols.Socks5))
                protocol = OutboundProxyProtocols.Http;

            return new WarpSettings(
                ReadBool(configuration, "Proxy:Warp:Enabled", false),
                Read(configuration, "Proxy:Warp:DockerSocketPath", "/var/run/docker.sock"),
                Read(configuration, "Proxy:Warp:Image", "caomingjun/warp:latest"),
                Read(configuration, "Proxy:Warp:Network", "telegram-panel_default"),
                NormalizeContainerPrefix(Read(configuration, "Proxy:Warp:ContainerPrefix", "telegram-panel-warp")),
                protocol,
                ReadInt(configuration, "Proxy:Warp:ContainerPort", 1080, 1, 65535),
                ReadInt(configuration, "Proxy:Warp:HostPortStart", 42080, 1024, 65000),
                mode,
                Read(configuration, "Proxy:Warp:ProxyHost", "127.0.0.1"),
                ReadBool(configuration, "Proxy:Warp:PullIfMissing", true));
        }

        private static string Read(IConfiguration configuration, string key, string fallback) =>
            string.IsNullOrWhiteSpace(configuration[key]) ? fallback : configuration[key]!.Trim();

        private static bool ReadBool(
            IConfiguration configuration,
            string key,
            bool fallback) =>
            bool.TryParse(configuration[key], out var value) ? value : fallback;

        private static int ReadInt(
            IConfiguration configuration,
            string key,
            int fallback,
            int min,
            int max) =>
            int.TryParse(configuration[key], out var value) && value >= min && value <= max
                ? value
                : fallback;

        private static string NormalizeContainerPrefix(string value)
        {
            var normalized = new string(value
                .Where(ch => char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.')
                .ToArray())
                .Trim('-', '.', '_');
            return string.IsNullOrWhiteSpace(normalized) ? "telegram-panel-warp" : normalized;
        }
    }

    internal interface IWarpDockerClientFactory
    {
        bool PlatformSupported { get; }
        IWarpDockerClient Create(string socketPath);
    }

    internal interface IWarpDockerClient : IDisposable
    {
        Task<string?> GetVersionAsync(CancellationToken cancellationToken);
        Task EnsureImageAsync(
            string image,
            bool pullIfMissing,
            CancellationToken cancellationToken);
        Task CreateVolumeAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken);
        Task<string> CreateContainerAsync(
            WarpSettings settings,
            string profileId,
            string containerName,
            string volumeName,
            int hostPort,
            CancellationToken cancellationToken);
        Task StartContainerAsync(string containerId, CancellationToken cancellationToken);
        Task StopContainerAsync(string containerId, CancellationToken cancellationToken);
        Task<bool> VerifyContainerOwnershipAsync(
            string containerId,
            string profileId,
            CancellationToken cancellationToken);
        Task<bool> VerifyVolumeOwnershipAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken);
        Task RemoveContainerAsync(string containerId, CancellationToken cancellationToken);
        Task RemoveVolumeAsync(string volumeName, CancellationToken cancellationToken);
    }

    private sealed class DockerEngineClientFactory : IWarpDockerClientFactory
    {
        public static DockerEngineClientFactory Instance { get; } = new();

        public bool PlatformSupported => OperatingSystem.IsLinux();

        public IWarpDockerClient Create(string socketPath) => new DockerEngineClient(socketPath);
    }

    private sealed class DockerEngineClient : IWarpDockerClient
    {
        private const int MaxDockerErrorBytes = 1024 * 1024;
        private static readonly TimeSpan InspectTimeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan MutationTimeout = TimeSpan.FromSeconds(30);
        private readonly HttpClient _http;
        private string _apiPrefix = string.Empty;

        public DockerEngineClient(string socketPath)
        {
            if (string.IsNullOrWhiteSpace(socketPath) || !Path.IsPathRooted(socketPath))
                throw new InvalidOperationException("Docker Socket 路径必须是绝对路径");

            var handler = new SocketsHttpHandler
            {
                ConnectTimeout = TimeSpan.FromSeconds(5),
                ConnectCallback = async (_, cancellationToken) =>
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    try
                    {
                        await socket.ConnectAsync(
                            new UnixDomainSocketEndPoint(socketPath),
                            cancellationToken);
                        return new NetworkStream(socket, ownsSocket: true);
                    }
                    catch
                    {
                        socket.Dispose();
                        throw;
                    }
                }
            };
            _http = new HttpClient(handler)
            {
                BaseAddress = new Uri("http://docker"),
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        public async Task<string?> GetVersionAsync(CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync("/version", timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            var value = await response.Content.ReadFromJsonAsync<DockerVersion>(
                cancellationToken: timeout.Token)
                ?? throw new InvalidDataException("Docker 版本响应为空");
            if (!string.IsNullOrWhiteSpace(value.ApiVersion))
                _apiPrefix = $"/v{value.ApiVersion}";
            return value.Version;
        }

        public async Task EnsureImageAsync(
            string image,
            bool pullIfMissing,
            CancellationToken cancellationToken)
        {
            using var inspectTimeout = TimeoutAfter(cancellationToken, InspectTimeout);
            using var inspect = await _http.GetAsync(
                Api($"/images/{Uri.EscapeDataString(image)}/json"),
                inspectTimeout.Token);
            if (inspect.IsSuccessStatusCode)
                return;
            if (inspect.StatusCode != HttpStatusCode.NotFound)
                await EnsureSuccessAsync(inspect, inspectTimeout.Token);
            if (!pullIfMissing)
                throw new InvalidOperationException($"Docker 镜像 {image} 不存在且未启用自动拉取");

            var (fromImage, tag) = SplitImage(image);
            var path = Api($"/images/create?fromImage={Uri.EscapeDataString(fromImage)}");
            if (!string.IsNullOrWhiteSpace(tag))
                path += $"&tag={Uri.EscapeDataString(tag)}";

            using var timeout = TimeoutAfter(cancellationToken, TimeSpan.FromMinutes(8));
            using var response = await _http.PostAsync(path, null, timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var reader = new StreamReader(stream);
            while (await reader.ReadLineAsync(timeout.Token) is { } line)
            {
                if (!line.Contains("\"error\"", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var document = JsonDocument.Parse(line);
                if (document.RootElement.TryGetProperty("error", out var error)
                    && !string.IsNullOrWhiteSpace(error.GetString()))
                {
                    throw new InvalidOperationException($"Docker 拉取镜像失败：{error.GetString()}");
                }
            }
        }

        public async Task CreateVolumeAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            var payload = new
            {
                Name = volumeName,
                Labels = Labels(profileId)
            };
            using var response = await _http.PostAsJsonAsync(
                Api("/volumes/create"),
                payload,
                timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);

            var value = await response.Content.ReadFromJsonAsync<DockerVolume>(
                cancellationToken: timeout.Token)
                ?? throw new InvalidDataException("Docker 卷创建响应为空");
            VerifyLabels(value.Labels, profileId, "Docker 卷");
        }

        public async Task<string> CreateContainerAsync(
            WarpSettings settings,
            string profileId,
            string containerName,
            string volumeName,
            int hostPort,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            var portKey = $"{settings.ContainerPort}/tcp";
            var portBindings = settings.ProxyHostMode == "published"
                ? new Dictionary<string, object>
                {
                    [portKey] = new[] { new { HostIp = "127.0.0.1", HostPort = hostPort.ToString() } }
                }
                : new Dictionary<string, object>();

            var payload = new
            {
                Image = settings.Image,
                Env = new[] { "WARP_SLEEP=2", $"GOST_ARGS=-L :{settings.ContainerPort}" },
                ExposedPorts = new Dictionary<string, object> { [portKey] = new { } },
                Labels = Labels(profileId),
                HostConfig = new
                {
                    NetworkMode = settings.Network,
                    PortBindings = portBindings,
                    CapAdd = new[] { "NET_ADMIN", "MKNOD", "AUDIT_WRITE" },
                    DeviceCgroupRules = new[] { "c 10:200 rwm" },
                    Sysctls = new Dictionary<string, string>
                    {
                        ["net.ipv6.conf.all.disable_ipv6"] = "0",
                        ["net.ipv4.conf.all.src_valid_mark"] = "1"
                    },
                    RestartPolicy = new { Name = "unless-stopped", MaximumRetryCount = 0 },
                    Mounts = new[]
                    {
                        new { Type = "volume", Source = volumeName, Target = "/var/lib/cloudflare-warp" }
                    },
                    LogConfig = new
                    {
                        Type = "json-file",
                        Config = new Dictionary<string, string>
                        {
                            ["max-size"] = "10m",
                            ["max-file"] = "2"
                        }
                    }
                }
            };

            using var response = await _http.PostAsJsonAsync(
                Api($"/containers/create?name={Uri.EscapeDataString(containerName)}"),
                payload,
                timeout.Token);
            await EnsureSuccessAsync(response, timeout.Token);
            var value = await response.Content.ReadFromJsonAsync<DockerCreateResponse>(
                cancellationToken: timeout.Token)
                ?? throw new InvalidDataException("Docker 容器创建响应为空");
            if (string.IsNullOrWhiteSpace(value.Id))
                throw new InvalidDataException("Docker 容器 ID 为空");
            return value.Id;
        }

        public async Task StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            using var response = await _http.PostAsync(
                Api($"/containers/{Uri.EscapeDataString(containerId)}/start"),
                null,
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.NotModified)
                await EnsureSuccessAsync(response, timeout.Token);
        }

        public async Task StopContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            using var response = await _http.PostAsync(
                Api($"/containers/{Uri.EscapeDataString(containerId)}/stop?t=10"),
                null,
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.NotModified)
                await EnsureSuccessAsync(response, timeout.Token);
        }

        public async Task<bool> VerifyContainerOwnershipAsync(
            string containerId,
            string profileId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, InspectTimeout);
            using var response = await _http.GetAsync(
                Api($"/containers/{Uri.EscapeDataString(containerId)}/json"),
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;
            await EnsureSuccessAsync(response, timeout.Token);
            var value = await response.Content.ReadFromJsonAsync<DockerContainerInspect>(
                cancellationToken: timeout.Token)
                ?? throw new InvalidDataException("Docker 容器检查响应为空");
            VerifyLabels(value.Config?.Labels, profileId, "Docker 容器");
            return true;
        }

        public async Task<bool> VerifyVolumeOwnershipAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, InspectTimeout);
            using var response = await _http.GetAsync(
                Api($"/volumes/{Uri.EscapeDataString(volumeName)}"),
                timeout.Token);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return false;
            await EnsureSuccessAsync(response, timeout.Token);
            var value = await response.Content.ReadFromJsonAsync<DockerVolume>(
                cancellationToken: timeout.Token)
                ?? throw new InvalidDataException("Docker 卷检查响应为空");
            VerifyLabels(value.Labels, profileId, "Docker 卷");
            return true;
        }

        public async Task RemoveContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            using var response = await _http.DeleteAsync(
                Api($"/containers/{Uri.EscapeDataString(containerId)}?force=true&v=false"),
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.NotFound)
                await EnsureSuccessAsync(response, timeout.Token);
        }

        public async Task RemoveVolumeAsync(
            string volumeName,
            CancellationToken cancellationToken)
        {
            using var timeout = TimeoutAfter(cancellationToken, MutationTimeout);
            using var response = await _http.DeleteAsync(
                Api($"/volumes/{Uri.EscapeDataString(volumeName)}"),
                timeout.Token);
            if (response.StatusCode != HttpStatusCode.NotFound)
                await EnsureSuccessAsync(response, timeout.Token);
        }

        public void Dispose() => _http.Dispose();

        private string Api(string path) => _apiPrefix + path;

        private static Dictionary<string, string> Labels(string profileId) => new()
        {
            [OwnerLabel] = "true",
            [ProfileLabel] = profileId
        };

        private static void VerifyLabels(
            IReadOnlyDictionary<string, string>? labels,
            string profileId,
            string resourceName)
        {
            if (labels == null
                || !labels.TryGetValue(OwnerLabel, out var owner)
                || !string.Equals(owner, "true", StringComparison.Ordinal)
                || !labels.TryGetValue(ProfileLabel, out var actualProfile)
                || !string.Equals(actualProfile, profileId, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{resourceName} 不属于当前 WARP 配置，拒绝操作");
            }
        }

        private static async Task EnsureSuccessAsync(
            HttpResponseMessage response,
            CancellationToken cancellationToken)
        {
            if (response.IsSuccessStatusCode)
                return;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var text = Encoding.UTF8.GetString(bytes.AsSpan(0, Math.Min(bytes.Length, MaxDockerErrorBytes)))
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Trim();
            if (text.Length > 500)
                text = text[..500];
            throw new InvalidOperationException(
                $"Docker API 返回 HTTP {(int)response.StatusCode}{(string.IsNullOrWhiteSpace(text) ? string.Empty : $"：{text}")}");
        }

        private static CancellationTokenSource TimeoutAfter(
            CancellationToken cancellationToken,
            TimeSpan timeout)
        {
            var source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            source.CancelAfter(timeout);
            return source;
        }

        private static (string Image, string? Tag) SplitImage(string value)
        {
            var digestIndex = value.IndexOf('@');
            if (digestIndex >= 0)
                return (value, null);

            var slash = value.LastIndexOf('/');
            var colon = value.LastIndexOf(':');
            return colon > slash
                ? (value[..colon], value[(colon + 1)..])
                : (value, "latest");
        }

        public sealed record DockerVersion(string? Version, string? ApiVersion);
        private sealed record DockerCreateResponse(string? Id);
        private sealed record DockerVolume(string? Name, Dictionary<string, string>? Labels);
        private sealed record DockerContainerConfig(Dictionary<string, string>? Labels);
        private sealed record DockerContainerInspect(DockerContainerConfig? Config);
    }
}
