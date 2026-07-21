using System.Net;
using System.Net.Sockets;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class WarpPortAllocationRegressionTests
{
    [Fact]
    public async Task 起始宿主端口已被占用时会自动跳到后续可用端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var hostPortStart = FindConsecutiveAvailablePorts();
        using var occupiedPort = new TcpListener(IPAddress.Any, hostPortStart);
        occupiedPort.Start();
        var docker = new WarpLifecycleRegressionTests.FakeWarpDockerClient();
        var manager = CreateManager(db, docker, hostPortStart);

        var proxy = await manager.CreateAsync(requestId: "occupied-port-skip");

        var profile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        var selectedPort = Assert.Single(docker.CreatedHostPorts);
        Assert.Equal(hostPortStart + 1, selectedPort);
        Assert.Equal(selectedPort, proxy.Port);
        Assert.Equal(proxy.Port, profile.HostPort);
        Assert.Equal("active", profile.Status);
        Assert.Empty(docker.RemovedContainerReferences);
    }

    [Fact]
    public async Task Docker启动前端口被抢占时会清理旧容器并自动换端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var hostPortStart = FindConsecutiveAvailablePorts();
        var docker = new WarpLifecycleRegressionTests.FakeWarpDockerClient();
        docker.StartErrors.Enqueue(new InvalidOperationException(
            "Docker API 返回 HTTP 500：Bind for 127.0.0.1 failed: port is already allocated"));
        var manager = CreateManager(db, docker, hostPortStart);

        var proxy = await manager.CreateAsync(requestId: "port-conflict-retry");

        var profile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal(new[] { hostPortStart, hostPortStart + 1 }, docker.CreatedHostPorts);
        Assert.Equal(
            new[] { "fake-container-1", "fake-container-2" },
            docker.StartAttemptedContainerReferences);
        Assert.Equal(hostPortStart + 1, proxy.Port);
        Assert.Equal(hostPortStart + 1, profile.HostPort);
        Assert.Equal("fake-container-2", profile.ContainerId);
        Assert.Equal("active", profile.Status);
        Assert.Equal(new[] { "fake-container-1" }, docker.RemovedContainerReferences);
        Assert.Equal(new[] { "fake-container-2" }, docker.Containers);
        Assert.Equal(new[] { "fake-container-2" }, docker.StartedContainerReferences);
        Assert.Equal(1, docker.CreateVolumeCalls);
        Assert.Single(docker.Volumes);
    }

    private static WarpContainerManager CreateManager(
        AppDbContext db,
        WarpLifecycleRegressionTests.FakeWarpDockerClient docker,
        int hostPortStart)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:Warp:Enabled"] = "true",
                ["Proxy:Warp:DockerSocketPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "fake-docker.sock"),
                ["Proxy:Warp:ProxyHostMode"] = "published",
                ["Proxy:Warp:ProxyHost"] = "127.0.0.1",
                ["Proxy:Warp:HostPortStart"] = hostPortStart.ToString()
            })
            .Build();
        return new WarpContainerManager(
            db,
            configuration,
            new SuccessfulProbeService(),
            NullLogger<WarpContainerManager>.Instance,
            new FakeDockerClientFactory(docker));
    }

    private static int FindConsecutiveAvailablePorts()
    {
        for (var port = 42080; port < 65000; port++)
        {
            try
            {
                using var first = new TcpListener(IPAddress.Any, port);
                using var second = new TcpListener(IPAddress.Any, port + 1);
                first.Start();
                second.Start();
                return port;
            }
            catch (SocketException)
            {
                // 继续寻找两个连续可用端口，避免依赖测试机当前占用情况。
            }
        }

        throw new InvalidOperationException("测试环境没有两个连续可用的宿主端口");
    }

    private sealed class FakeDockerClientFactory : WarpContainerManager.IWarpDockerClientFactory
    {
        private readonly WarpContainerManager.IWarpDockerClient _client;

        public FakeDockerClientFactory(WarpContainerManager.IWarpDockerClient client)
        {
            _client = client;
        }

        public bool PlatformSupported => true;

        public WarpContainerManager.IWarpDockerClient Create(string socketPath) => _client;
    }

    private sealed class SuccessfulProbeService : IProxyEgressProbeService
    {
        public Task<EgressProbeResult> ProbePanelAsync(
            CancellationToken cancellationToken = default) =>
            SuccessAsync(cancellationToken);

        public Task<EgressProbeResult> ProbeProxyAsync(
            OutboundProxy proxy,
            string stableAccountKey,
            CancellationToken cancellationToken = default) =>
            SuccessAsync(cancellationToken);

        public Task<EgressProbeResult> ProbeProxyAsync(
            ProxyConnectionOptions options,
            bool requireWarp = false,
            CancellationToken cancellationToken = default) =>
            SuccessAsync(cancellationToken);

        private static Task<EgressProbeResult> SuccessAsync(
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new EgressProbeResult(
                true,
                "104.16.0.1",
                "US",
                null,
                "Cloudflare",
                "on",
                10,
                DateTime.UtcNow,
                null));
        }
    }
}
