using System.Net;
using System.Net.Sockets;
using System.Text;
using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Data.Entities;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ProxyTcpConnectorTests
{
    [Fact]
    public async Task HttpConnect_发送目标与Basic认证()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            var header = await ReadHeaderAsync(stream);
            Assert.Contains("CONNECT telegram.example:443 HTTP/1.1", header);
            var token = Convert.ToBase64String(Encoding.UTF8.GetBytes("resin-user:resin-token"));
            Assert.Contains($"Proxy-Authorization: Basic {token}", header);
            await stream.WriteAsync("HTTP/1.1 200 Connection Established\r\n\r\n"u8.ToArray());
        });

        var options = new ProxyConnectionOptions(
            1,
            "HTTP",
            OutboundProxyKinds.Manual,
            OutboundProxyProtocols.Http,
            IPAddress.Loopback.ToString(),
            port,
            "resin-user",
            "resin-token",
            null);
        using var client = await ProxyTcpConnector.ConnectAsync("telegram.example", 443, options);
        Assert.True(client.Connected);
        await serverTask;
    }

    [Fact]
    public async Task Socks5_完成用户名密码认证与域名Connect()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(async () =>
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            var stream = accepted.GetStream();
            Assert.Equal(new byte[] { 5, 2, 0, 2 }, await ReadExactlyAsync(stream, 4));
            await stream.WriteAsync(new byte[] { 5, 2 });

            var authHead = await ReadExactlyAsync(stream, 2);
            Assert.Equal(1, authHead[0]);
            var username = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, authHead[1]));
            var passwordLength = (await ReadExactlyAsync(stream, 1))[0];
            var password = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, passwordLength));
            Assert.Equal("user", username);
            Assert.Equal("pass", password);
            await stream.WriteAsync(new byte[] { 1, 0 });

            Assert.Equal(new byte[] { 5, 1, 0, 3 }, await ReadExactlyAsync(stream, 4));
            var hostLength = (await ReadExactlyAsync(stream, 1))[0];
            var host = Encoding.UTF8.GetString(await ReadExactlyAsync(stream, hostLength));
            var targetPort = await ReadExactlyAsync(stream, 2);
            Assert.Equal("telegram.example", host);
            Assert.Equal(443, (targetPort[0] << 8) | targetPort[1]);
            await stream.WriteAsync(new byte[] { 5, 0, 0, 1, 127, 0, 0, 1, 0, 0 });
        });

        var options = new ProxyConnectionOptions(
            2,
            "SOCKS5",
            OutboundProxyKinds.Manual,
            OutboundProxyProtocols.Socks5,
            IPAddress.Loopback.ToString(),
            port,
            "user",
            "pass",
            null);
        using var client = await ProxyTcpConnector.ConnectAsync("telegram.example", 443, options);
        Assert.True(client.Connected);
        await serverTask;
    }

    [Theory]
    [InlineData("telegram.example\r\nX-Injected: true")]
    [InlineData("telegram.example/path")]
    [InlineData("[2001:db8::1")]
    public async Task Connect_在连接代理前拒绝非法目标主机(string targetHost)
    {
        var options = new ProxyConnectionOptions(
            3,
            "HTTP",
            OutboundProxyKinds.Manual,
            OutboundProxyProtocols.Http,
            IPAddress.Loopback.ToString(),
            1,
            null,
            null,
            null);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ProxyTcpConnector.ConnectAsync(targetHost, 443, options));
    }

    [Fact]
    public void Resin_按账号生成稳定粘性身份()
    {
        var proxy = new OutboundProxy
        {
            Id = 7,
            Name = "Resin",
            Kind = OutboundProxyKinds.Resin,
            Protocol = OutboundProxyProtocols.Http,
            Host = "resin",
            Port = 2260,
            Username = "不应复用",
            Password = "proxy-token",
            ResinPlatform = "Telegram"
        };

        var options = AccountProxyResolver.BuildConnectionOptions(proxy, "tg account 42");

        Assert.Equal("Telegram.tg_account_42", options.Username);
        Assert.Equal("proxy-token", options.Password);
    }

    private static async Task<string> ReadHeaderAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        while (bytes.Count < 32 * 1024)
        {
            var value = (await ReadExactlyAsync(stream, 1))[0];
            bytes.Add(value);
            if (bytes.Count >= 4
                && bytes[^4] == '\r'
                && bytes[^3] == '\n'
                && bytes[^2] == '\r'
                && bytes[^1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray());
            }
        }
        throw new InvalidDataException("测试代理请求头过长");
    }

    private static async Task<byte[]> ReadExactlyAsync(Stream stream, int length)
    {
        var result = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(result.AsMemory(offset));
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
        return result;
    }
}
