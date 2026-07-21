using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using TelegramPanel.Web.Api;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class WarpLifecycleRegressionTests
{
    [Fact]
    public async Task 已删除WARP记录允许复用历史主机端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WarpProfiles.Add(Profile("deleted-profile", "deleted", 42080));
        await db.SaveChangesAsync();

        db.WarpProfiles.Add(Profile("active-profile", "active", 42080));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.WarpProfiles.CountAsync());
    }

    [Fact]
    public async Task 未删除WARP记录仍禁止重复主机端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WarpProfiles.Add(Profile("active-profile", "active", 42080));
        await db.SaveChangesAsync();

        db.WarpProfiles.Add(Profile("starting-profile", "starting", 42080));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task 创建失败的WARP记录允许复用历史主机端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WarpProfiles.Add(Profile("failed-profile", "failed", 42080));
        await db.SaveChangesAsync();

        db.WarpProfiles.Add(Profile("active-profile", "active", 42080));
        await db.SaveChangesAsync();

        Assert.Equal(2, await db.WarpProfiles.CountAsync());
    }

    [Fact]
    public async Task 待清理WARP记录会继续占用主机端口()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.WarpProfiles.Add(Profile("cleanup-pending-profile", "cleanup_pending", 42080));
        await db.SaveChangesAsync();

        db.WarpProfiles.Add(Profile("new-profile", "creating", 42080));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task WARP显示名超长时在创建资源前拒绝()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var manager = new WarpContainerManager(
            db,
            configuration,
            new ProxyEgressProbeService(),
            NullLogger<WarpContainerManager>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.CreateAsync(new string('x', 201)));
        Assert.Empty(await db.WarpProfiles.ToListAsync());
    }

    [Fact]
    public async Task WARP基础配置默认关闭()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection()
            .Build();
        var manager = new WarpContainerManager(
            db,
            configuration,
            new ProxyEgressProbeService(),
            NullLogger<WarpContainerManager>.Instance);

        var status = await manager.GetStatusAsync();

        Assert.False(status.Enabled);
    }

    [Fact]
    public async Task WARP运行状态公开已配置的默认协议()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        var manager = CreateManager(
            db,
            new FakeWarpDockerClient(),
            enabled: false,
            defaultProtocol: "socks5");

        var status = await manager.GetStatusAsync();

        Assert.Equal("socks5", status.DefaultProtocol);
    }

    [Fact]
    public async Task 单次创建WARP可以覆盖系统默认协议()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var docker = new FakeWarpDockerClient
        {
            StartError = new InvalidOperationException("stop after capturing settings")
        };
        var manager = CreateManager(
            db,
            docker,
            enabled: true,
            defaultProtocol: "http");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "protocol-override", protocol: " SOCKS5 "));

        Assert.Equal("socks5", docker.LastCreatedProtocol);
        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task 单次创建WARP拒绝不支持的协议且不创建资源()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var docker = new FakeWarpDockerClient();
        var manager = CreateManager(db, docker, enabled: true);

        var error = await Assert.ThrowsAsync<ArgumentException>(() =>
            manager.CreateAsync(protocol: "mtproto"));

        Assert.Contains("HTTP 或 SOCKS5", error.Message);
        Assert.Null(docker.LastCreatedProtocol);
        Assert.Empty(await db.WarpProfiles.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task 相同请求键不能把既有WARP静默改成另一协议()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "existing-warp",
            Kind = "warp",
            Protocol = "http",
            Host = "existing-warp",
            Port = 1080,
            IsEnabled = true
        };
        var profile = Profile("existing-profile", "active", 42080);
        profile.RequestId = "same-request";
        profile.Proxy = proxy;
        db.WarpProfiles.Add(profile);
        await db.SaveChangesAsync();
        var manager = CreateManager(db, new FakeWarpDockerClient(), enabled: false);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "same-request", protocol: "socks5"));

        Assert.Contains("不能改为 SOCKS5", error.Message);
        Assert.Equal("http", (await db.OutboundProxies.AsNoTracking().SingleAsync()).Protocol);
    }

    [Fact]
    public void WARP代理接口公开独立容器的连接状态()
    {
        var proxy = new OutboundProxy
        {
            Id = 7,
            Name = "warp-proxy",
            Kind = "warp",
            Protocol = "socks5",
            Host = "warp-proxy",
            Port = 1080,
            IsEnabled = true,
            WarpProfile = new WarpProfile
            {
                ProfileId = "profile-7",
                ContainerName = "warp-proxy",
                VolumeName = "warp-data-7",
                HostPort = 42080,
                Status = "active",
                WarpStatus = "on"
            }
        };

        var dto = ProxyApiEndpoints.ToDto(proxy);

        Assert.Equal("on", dto.WarpStatus);
    }

    [Fact]
    public async Task 创建失败且补偿成功会删除恢复入口并释放请求键()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var docker = new FakeWarpDockerClient
        {
            StartError = new InvalidOperationException("start failed")
        };
        var manager = CreateManager(db, docker, enabled: true);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "cleanup-success"));

        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
        Assert.Empty(await db.WarpProfiles.AsNoTracking().ToListAsync());
        Assert.Empty(docker.Containers);
        Assert.Empty(docker.Volumes);
    }

    [Fact]
    public async Task 创建失败且补偿失败会保留禁用代理供删除重试()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var docker = new FakeWarpDockerClient
        {
            StartError = new InvalidOperationException("start failed"),
            RemoveContainerError = new InvalidOperationException("container remove failed"),
            RemoveVolumeError = new InvalidOperationException("volume remove failed")
        };
        var manager = CreateManager(db, docker, enabled: true);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "cleanup-failed"));

        Assert.Contains("已保留禁用代理", error.Message);
        var proxy = await db.OutboundProxies
            .AsNoTracking()
            .Include(x => x.WarpProfile)
            .SingleAsync();
        Assert.False(proxy.IsEnabled);
        Assert.Equal("fail", proxy.TestStatus);
        Assert.NotNull(proxy.WarpProfile);
        // 补偿失败时资源仍可能占用宿主端口，必须保持 cleanup_pending，
        // 避免被“failed”状态的端口过滤索引错误地再次分配。
        Assert.Equal("cleanup_pending", proxy.WarpProfile!.Status);
        Assert.Equal("cleanup-failed", proxy.WarpProfile.RequestId);
        Assert.False(proxy.WarpProfile.DesiredEnabled);
        Assert.False(string.IsNullOrWhiteSpace(proxy.WarpProfile.ContainerId));
        Assert.NotEmpty(docker.Containers);
        Assert.NotEmpty(docker.Volumes);

        docker.RemoveContainerError = null;
        docker.RemoveVolumeError = null;
        var service = CreateProxyService(db, manager);
        await service.DeleteAsync(proxy.Id);

        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
        var deletedProfile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("deleted", deletedProfile.Status);
        Assert.Null(deletedProfile.OutboundProxyId);
        Assert.Empty(docker.Containers);
        Assert.Empty(docker.Volumes);
    }

    [Fact]
    public async Task 删除资源时ContainerId为空会按名称校验并删除()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var proxy = new OutboundProxy
        {
            Name = "recover-by-name",
            Kind = "warp",
            Protocol = "http",
            Host = "recover-by-name",
            Port = 1080,
            IsEnabled = false,
            TestStatus = "fail"
        };
        var profile = new WarpProfile
        {
            ProfileId = "recover-by-name-profile",
            RequestId = "recover-by-name-request",
            ContainerName = "recover-by-name-container",
            ContainerId = null,
            VolumeName = "recover-by-name-volume",
            HostPort = 42201,
            Status = "failed",
            DesiredEnabled = false,
            Proxy = proxy
        };
        db.WarpProfiles.Add(profile);
        await db.SaveChangesAsync();
        var docker = new FakeWarpDockerClient();
        docker.SeedResources(
            profile.ProfileId,
            profile.ContainerName,
            profile.VolumeName,
            "docker-generated-id");
        var manager = CreateManager(db, docker, enabled: true);

        await CreateProxyService(db, manager).DeleteAsync(proxy.Id);

        Assert.Contains(profile.ContainerName, docker.RemovedContainerReferences);
        Assert.Empty(docker.Containers);
        Assert.Empty(docker.Volumes);
    }

    [Fact]
    public async Task 容器删除成功但卷删除失败会保留禁用WARP供重试清理()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var proxy = new OutboundProxy
        {
            Name = "partial-delete",
            Kind = "warp",
            Protocol = "http",
            Host = "partial-delete",
            Port = 1080,
            IsEnabled = true,
            TestStatus = "ok"
        };
        var profile = new WarpProfile
        {
            ProfileId = "partial-delete-profile",
            RequestId = "partial-delete-request",
            ContainerName = "partial-delete-container",
            ContainerId = "partial-delete-container-id",
            VolumeName = "partial-delete-volume",
            HostPort = 42202,
            Status = "active",
            DesiredEnabled = true,
            Proxy = proxy
        };
        db.WarpProfiles.Add(profile);
        await db.SaveChangesAsync();

        var docker = new FakeWarpDockerClient
        {
            RemoveVolumeError = new InvalidOperationException("volume remove failed")
        };
        docker.SeedResources(
            profile.ProfileId,
            profile.ContainerName,
            profile.VolumeName,
            profile.ContainerId);
        var service = CreateProxyService(db, CreateManager(db, docker, enabled: true));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.DeleteAsync(proxy.Id));

        Assert.Contains("WARP 资源清理失败", error.Message);
        Assert.Empty(docker.Containers);
        Assert.Single(docker.Volumes);
        var retainedProxy = await db.OutboundProxies.AsNoTracking().SingleAsync();
        Assert.False(retainedProxy.IsEnabled);
        Assert.Equal("fail", retainedProxy.TestStatus);
        var retainedProfile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("deleting", retainedProfile.Status);
        Assert.False(retainedProfile.DesiredEnabled);

        docker.RemoveVolumeError = null;
        await service.DeleteAsync(proxy.Id);

        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
        Assert.Empty(docker.Containers);
        Assert.Empty(docker.Volumes);
        var deletedProfile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("deleted", deletedProfile.Status);
        Assert.Null(deletedProfile.OutboundProxyId);
    }

    [Fact]
    public async Task 同请求历史失败Profile会先严格清理且失败时保留请求键()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var profile = new WarpProfile
        {
            ProfileId = "historical-failed-profile",
            RequestId = "same-request",
            ContainerName = "historical-failed-container",
            VolumeName = "historical-failed-volume",
            HostPort = 42199,
            Status = "failed",
            DesiredEnabled = false
        };
        db.WarpProfiles.Add(profile);
        await db.SaveChangesAsync();
        var docker = new FakeWarpDockerClient
        {
            RemoveContainerError = new InvalidOperationException("container remove failed"),
            RemoveVolumeError = new InvalidOperationException("volume remove failed")
        };
        docker.SeedResources(
            profile.ProfileId,
            profile.ContainerName,
            profile.VolumeName,
            "historical-container-id");
        var manager = CreateManager(db, docker, enabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "same-request"));

        var retained = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("same-request", retained.RequestId);
        Assert.Equal(0, docker.CreateVolumeCalls);

        docker.RemoveContainerError = null;
        docker.RemoveVolumeError = null;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.CreateAsync(requestId: "same-request"));

        Assert.Empty(await db.WarpProfiles.AsNoTracking().ToListAsync());
        Assert.Empty(docker.Containers);
        Assert.Empty(docker.Volumes);
        Assert.Equal(0, docker.CreateVolumeCalls);
    }

    internal static WarpContainerManager CreateManager(
        AppDbContext db,
        FakeWarpDockerClient docker,
        bool enabled,
        string defaultProtocol = "http")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:Warp:Enabled"] = enabled.ToString(),
                ["Proxy:Warp:DockerSocketPath"] = Path.Combine(Path.GetTempPath(), "fake-docker.sock"),
                ["Proxy:Warp:Protocol"] = defaultProtocol
            })
            .Build();
        return new WarpContainerManager(
            db,
            configuration,
            new ProxyEgressProbeService(),
            NullLogger<WarpContainerManager>.Instance,
            new FakeWarpDockerClientFactory(docker));
    }

    private static ProxyManagementService CreateProxyService(
        AppDbContext db,
        WarpContainerManager manager) =>
        new(
            db,
            new NoopClientPool(),
            new ProxyEgressProbeService(),
            manager,
            NullLogger<ProxyManagementService>.Instance);

    private static WarpProfile Profile(string profileId, string status, int hostPort) => new()
    {
        ProfileId = profileId,
        ContainerName = $"container-{profileId}",
        VolumeName = $"volume-{profileId}",
        HostPort = hostPort,
        Status = status,
        DesiredEnabled = status is not "deleted"
    };

    private sealed class FakeWarpDockerClientFactory : WarpContainerManager.IWarpDockerClientFactory
    {
        private readonly FakeWarpDockerClient _client;

        public FakeWarpDockerClientFactory(FakeWarpDockerClient client)
        {
            _client = client;
        }

        public bool PlatformSupported => true;

        public WarpContainerManager.IWarpDockerClient Create(string socketPath) => _client;
    }

    internal sealed class FakeWarpDockerClient : WarpContainerManager.IWarpDockerClient
    {
        private readonly List<ContainerResource> _containers = new();
        private readonly Dictionary<string, string> _volumes = new(StringComparer.Ordinal);
        private int _nextContainerId;

        public Exception? StartError { get; set; }
        public Exception? StopError { get; set; }
        public Exception? RemoveContainerError { get; set; }
        public Exception? RemoveVolumeError { get; set; }
        public int CreateVolumeCalls { get; private set; }
        public string? LastCreatedProtocol { get; private set; }
        public IReadOnlyCollection<string> Containers => _containers.Select(x => x.Id).ToArray();
        public IReadOnlyCollection<string> Volumes => _volumes.Keys.ToArray();
        public List<string> StartedContainerReferences { get; } = new();
        public List<string> StoppedContainerReferences { get; } = new();
        public List<string> RemovedContainerReferences { get; } = new();

        public void SeedResources(
            string profileId,
            string containerName,
            string volumeName,
            string containerId)
        {
            _containers.Add(new ContainerResource(containerId, containerName, profileId));
            _volumes[volumeName] = profileId;
        }

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<string?>("fake-docker");
        }

        public Task EnsureImageAsync(
            string image,
            bool pullIfMissing,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public Task CreateVolumeAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CreateVolumeCalls++;
            _volumes[volumeName] = profileId;
            return Task.CompletedTask;
        }

        public Task<string> CreateContainerAsync(
            WarpContainerManager.WarpSettings settings,
            string profileId,
            string containerName,
            string volumeName,
            int hostPort,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCreatedProtocol = settings.Protocol;
            var id = $"fake-container-{++_nextContainerId}";
            _containers.Add(new ContainerResource(id, containerName, profileId));
            return Task.FromResult(id);
        }

        public Task StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StartError != null)
                throw StartError;
            StartedContainerReferences.Add(containerId);
            return Task.CompletedTask;
        }

        public Task StopContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (StopError != null)
                throw StopError;
            StoppedContainerReferences.Add(containerId);
            return Task.CompletedTask;
        }

        public Task<bool> VerifyContainerOwnershipAsync(
            string containerId,
            string profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var container = _containers.FirstOrDefault(
                x => x.Id == containerId || x.Name == containerId);
            if (container == null)
                return Task.FromResult(false);
            if (!string.Equals(container.ProfileId, profileId, StringComparison.Ordinal))
                throw new InvalidOperationException("Docker 容器不属于当前 WARP 配置");
            return Task.FromResult(true);
        }

        public Task<bool> VerifyVolumeOwnershipAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!_volumes.TryGetValue(volumeName, out var actualProfileId))
                return Task.FromResult(false);
            if (!string.Equals(actualProfileId, profileId, StringComparison.Ordinal))
                throw new InvalidOperationException("Docker 卷不属于当前 WARP 配置");
            return Task.FromResult(true);
        }

        public Task RemoveContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RemoveContainerError != null)
                throw RemoveContainerError;
            var container = _containers.FirstOrDefault(
                x => x.Id == containerId || x.Name == containerId);
            if (container != null)
                _containers.Remove(container);
            RemovedContainerReferences.Add(containerId);
            return Task.CompletedTask;
        }

        public Task RemoveVolumeAsync(
            string volumeName,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (RemoveVolumeError != null)
                throw RemoveVolumeError;
            _volumes.Remove(volumeName);
            return Task.CompletedTask;
        }

        public void Dispose()
        {
        }

        private sealed record ContainerResource(string Id, string Name, string ProfileId);
    }

    private sealed class NoopClientPool : ITelegramClientPool
    {
        public int ActiveClientCount => 0;

        public Task<Client> GetOrCreateClientAsync(
            int accountId,
            int apiId,
            string apiHash,
            string sessionPath,
            string? sessionKey = null,
            string? phoneNumber = null,
            long? userId = null) =>
            throw new NotSupportedException();

        public Client? GetClient(int accountId) => null;
        public Task RemoveClientAsync(int accountId) => Task.CompletedTask;
        public Task RemoveAllClientsAsync() => Task.CompletedTask;
        public bool IsClientConnected(int accountId) => false;
    }
}
