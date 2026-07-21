using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TelegramPanel.Web.Services;
using WTelegram;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class AccountExportProxyRoutingTests
{
    private const string ApiHash = "0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task 已有代理会应用到独立导出客户端()
    {
        var proxy = NewProxy(OutboundProxyProtocols.Http);
        var resolver = new StubProxyResolver(new AccountProxyResolution(proxy, false));
        var service = CreateService(resolver);
        var sessionPath = NewSessionPath();

        try
        {
            var resolved = await service.ResolveIndependentExportProxyAsync(42);
            Assert.Same(proxy, resolved);
            Assert.Equal(42, resolver.LastAccountId);

            using var client = service.CreateIndependentExportClient(
                NewAccount(42),
                ApiHash,
                sessionPath,
                twoFactorPassword: null,
                resolved);
            Assert.NotNull(client.TcpHandler.Target);
            Assert.Null(client.MTProxyUrl);
        }
        finally
        {
            TryDelete(sessionPath);
        }
    }

    [Fact]
    public async Task 显式直连会忽略已配置的全局代理()
    {
        var resolver = new StubProxyResolver(new AccountProxyResolution(null, false));
        var service = CreateService(resolver, GlobalProxyConfiguration());
        var sessionPath = NewSessionPath();

        try
        {
            var resolved = await service.ResolveIndependentExportProxyAsync(43);
            Assert.Null(resolved);

            using var client = service.CreateIndependentExportClient(
                NewAccount(43),
                ApiHash,
                sessionPath,
                twoFactorPassword: null,
                resolved);
            Assert.Null(client.TcpHandler.Target);
            Assert.Null(client.MTProxyUrl);
        }
        finally
        {
            TryDelete(sessionPath);
        }
    }

    [Fact]
    public async Task 全局模式会通过统一连接器应用全局代理()
    {
        var resolver = new StubProxyResolver(new AccountProxyResolution(null, true));
        var service = CreateService(resolver, GlobalProxyConfiguration());
        var sessionPath = NewSessionPath();

        try
        {
            var resolved = await service.ResolveIndependentExportProxyAsync(44);
            Assert.NotNull(resolved);
            Assert.Equal(0, resolved!.ProxyId);
            Assert.Equal(OutboundProxyProtocols.Socks5, resolved.Protocol);
            Assert.Equal("127.0.0.9", resolved.Host);
            Assert.Equal(19080, resolved.Port);
            Assert.Equal("global-user", resolved.Username);
            Assert.Equal("global-password", resolved.Password);

            using var client = service.CreateIndependentExportClient(
                NewAccount(44),
                ApiHash,
                sessionPath,
                twoFactorPassword: null,
                resolved);
            Assert.NotNull(client.TcpHandler.Target);
            Assert.Null(client.MTProxyUrl);
        }
        finally
        {
            TryDelete(sessionPath);
        }
    }

    [Fact]
    public async Task 全局模式缺少配置时独立导出会闭锁而不是直连()
    {
        var resolver = new StubProxyResolver(new AccountProxyResolution(null, true));
        var service = CreateService(resolver);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveIndependentExportProxyAsync(46));

        Assert.Contains("阻止降级为直连", error.Message);
        Assert.Equal(46, resolver.LastAccountId);
    }

    [Fact]
    public async Task 停用代理解析失败时不会回退全局代理()
    {
        var resolver = new StubProxyResolver(
            new InvalidOperationException("账号 45 绑定的代理不可用，已阻止降级为直连"));
        var service = CreateService(resolver, GlobalProxyConfiguration());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ResolveIndependentExportProxyAsync(45));

        Assert.Contains("阻止降级", error.Message);
        Assert.Equal(45, resolver.LastAccountId);
    }

    private static AccountExportService CreateService(
        IAccountProxyResolver resolver,
        IConfiguration? configuration = null)
    {
        configuration ??= new ConfigurationBuilder().Build();
        return new AccountExportService(
            configuration,
            NullLogger<AccountExportService>.Instance,
            new StubClientPool(),
            resolver,
            new SessionPathResolver(configuration));
    }

    private static IConfiguration GlobalProxyConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telegram:Proxy:Server"] = "127.0.0.9",
                ["Telegram:Proxy:Port"] = "19080",
                ["Telegram:Proxy:Username"] = "global-user",
                ["Telegram:Proxy:Password"] = "global-password"
            })
            .Build();

    private static ProxyConnectionOptions NewProxy(string protocol) => new(
        7,
        "account-proxy",
        OutboundProxyKinds.Manual,
        protocol,
        "127.0.0.1",
        18080,
        "user",
        "password",
        null);

    private static Account NewAccount(int id) => new()
    {
        Id = id,
        Phone = "8613800000000",
        UserId = 10001,
        SessionPath = "unused.session",
        ApiId = 12345,
        ApiHash = ApiHash
    };

    private static string NewSessionPath() =>
        Path.Combine(Path.GetTempPath(), $"telegram-panel-export-proxy-{Guid.NewGuid():N}.session");

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed class StubProxyResolver : IAccountProxyResolver
    {
        private readonly AccountProxyResolution? _resolution;
        private readonly Exception? _error;

        public StubProxyResolver(AccountProxyResolution resolution)
        {
            _resolution = resolution;
        }

        public StubProxyResolver(Exception error)
        {
            _error = error;
        }

        public int? LastAccountId { get; private set; }

        public Task<AccountProxyResolution> ResolveAsync(
            int accountId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastAccountId = accountId;
            return _error != null
                ? Task.FromException<AccountProxyResolution>(_error)
                : Task.FromResult(_resolution!);
        }
    }

    private sealed class StubClientPool : ITelegramClientPool
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
