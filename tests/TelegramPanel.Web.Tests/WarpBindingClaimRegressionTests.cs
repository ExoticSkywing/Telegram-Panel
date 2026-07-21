using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data;
using TelegramPanel.Data.Entities;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class WarpBindingClaimRegressionTests
{
    [Fact]
    public async Task 逐账号创建WARP会持有请求占用直到失败补偿删除结束()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new AppDbContext(options);
        await db.Database.EnsureCreatedAsync();

        var account = new Account
        {
            Phone = "8613800000888",
            UserId = 888,
            SessionPath = "sessions/warp-binding-claim.session",
            ApiId = 1,
            ApiHash = "hash",
            IsActive = true
        };
        db.Accounts.Add(account);
        await db.SaveChangesAsync();

        var claims = new TemporaryWarpClaimStore();
        string? requestId = null;
        bool? claimedDuringBinding = null;
        bool? claimedDuringCompensation = null;
        var innerDocker = new WarpLifecycleRegressionTests.FakeWarpDockerClient();
        var docker = new ObservingDockerClient(innerDocker, async () =>
        {
            var cleanupRequestId = await db.WarpProfiles
                .AsNoTracking()
                .Select(x => x.RequestId)
                .SingleAsync();
            claimedDuringCompensation = claims.OwnsRequest(cleanupRequestId);
        });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Proxy:Warp:Enabled"] = "true",
                ["Proxy:Warp:DockerSocketPath"] = Path.Combine(
                    Path.GetTempPath(),
                    "fake-docker.sock"),
                ["Proxy:Warp:ProxyHostMode"] = "container"
            })
            .Build();
        var probe = new SuccessfulProbeService();
        var manager = new WarpContainerManager(
            db,
            configuration,
            probe,
            NullLogger<WarpContainerManager>.Instance,
            new FakeDockerClientFactory(docker));
        var clientPool = new RejectingClientPool(async () =>
        {
            requestId = await db.WarpProfiles
                .AsNoTracking()
                .Select(x => x.RequestId)
                .SingleAsync();
            claimedDuringBinding = claims.OwnsRequest(requestId);
            throw new InvalidOperationException("模拟绑定前旧客户端无法断开");
        });
        var service = new ProxyManagementService(
            db,
            clientPool,
            probe,
            manager,
            NullLogger<ProxyManagementService>.Instance,
            configuration,
            claims);

        var result = await service.BindAccountsAsync(
            new[] { account.Id },
            new AccountProxyBindingInput("warp_per_account"));

        Assert.Equal(0, result.Success);
        Assert.Equal(1, result.Failed);
        Assert.True(claimedDuringBinding);
        Assert.True(claimedDuringCompensation);
        Assert.NotNull(requestId);
        Assert.False(claims.OwnsRequest(requestId));
        Assert.Empty(await db.OutboundProxies.AsNoTracking().ToListAsync());
        var deletedProfile = await db.WarpProfiles.AsNoTracking().SingleAsync();
        Assert.Equal("deleted", deletedProfile.Status);
        Assert.Null(deletedProfile.OutboundProxyId);
        Assert.Empty(innerDocker.Containers);
        Assert.Empty(innerDocker.Volumes);
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

    private sealed class ObservingDockerClient : WarpContainerManager.IWarpDockerClient
    {
        private readonly WarpContainerManager.IWarpDockerClient _inner;
        private readonly Func<Task> _onRemoveContainer;

        public ObservingDockerClient(
            WarpContainerManager.IWarpDockerClient inner,
            Func<Task> onRemoveContainer)
        {
            _inner = inner;
            _onRemoveContainer = onRemoveContainer;
        }

        public Task<string?> GetVersionAsync(CancellationToken cancellationToken) =>
            _inner.GetVersionAsync(cancellationToken);

        public Task EnsureImageAsync(
            string image,
            bool pullIfMissing,
            CancellationToken cancellationToken) =>
            _inner.EnsureImageAsync(image, pullIfMissing, cancellationToken);

        public Task CreateVolumeAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken) =>
            _inner.CreateVolumeAsync(volumeName, profileId, cancellationToken);

        public Task<string> CreateContainerAsync(
            WarpContainerManager.WarpSettings settings,
            string profileId,
            string containerName,
            string volumeName,
            int hostPort,
            CancellationToken cancellationToken) =>
            _inner.CreateContainerAsync(
                settings,
                profileId,
                containerName,
                volumeName,
                hostPort,
                cancellationToken);

        public Task StartContainerAsync(
            string containerId,
            CancellationToken cancellationToken) =>
            _inner.StartContainerAsync(containerId, cancellationToken);

        public Task StopContainerAsync(
            string containerId,
            CancellationToken cancellationToken) =>
            _inner.StopContainerAsync(containerId, cancellationToken);

        public Task RestartContainerAsync(
            string containerId,
            CancellationToken cancellationToken) =>
            _inner.RestartContainerAsync(containerId, cancellationToken);

        public Task<bool> VerifyContainerOwnershipAsync(
            string containerId,
            string profileId,
            CancellationToken cancellationToken) =>
            _inner.VerifyContainerOwnershipAsync(containerId, profileId, cancellationToken);

        public Task<bool> VerifyVolumeOwnershipAsync(
            string volumeName,
            string profileId,
            CancellationToken cancellationToken) =>
            _inner.VerifyVolumeOwnershipAsync(volumeName, profileId, cancellationToken);

        public async Task RemoveContainerAsync(
            string containerId,
            CancellationToken cancellationToken)
        {
            await _onRemoveContainer();
            await _inner.RemoveContainerAsync(containerId, cancellationToken);
        }

        public Task RemoveVolumeAsync(
            string volumeName,
            CancellationToken cancellationToken) =>
            _inner.RemoveVolumeAsync(volumeName, cancellationToken);

        public void Dispose()
        {
            // 测试工厂重用同一个假客户端，不在单次操作后释放。
        }
    }

    private sealed class RejectingClientPool : ITelegramClientPool
    {
        private readonly Func<Task> _onStrictRemove;

        public RejectingClientPool(Func<Task> onStrictRemove)
        {
            _onStrictRemove = onStrictRemove;
        }

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

        public Task RemoveClientStrictAsync(int accountId) => _onStrictRemove();

        public Task RemoveAllClientsAsync() => Task.CompletedTask;

        public bool IsClientConnected(int accountId) => false;
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
