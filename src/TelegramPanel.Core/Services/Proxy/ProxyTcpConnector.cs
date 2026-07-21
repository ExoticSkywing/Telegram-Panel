using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Services.Proxy;

/// <summary>
/// 为 WTelegram 提供 HTTP CONNECT 和 SOCKS5 代理连接。
/// </summary>
public static class ProxyTcpConnector
{
    private const int MaxHttpHeaderBytes = 32 * 1024;
    private static readonly byte[] HttpHeaderTerminator = { 13, 10, 13, 10 };

    public static async Task<TcpClient> ConnectAsync(
        string targetHost,
        int targetPort,
        ProxyConnectionOptions proxy,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        targetHost = NormalizeTargetHost(targetHost);
        if (targetPort is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(targetPort));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));

        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(proxy.Host, proxy.Port, timeout.Token);
            client.NoDelay = true;

            switch (proxy.Protocol)
            {
                case OutboundProxyProtocols.Http:
                    await EstablishHttpTunnelAsync(
                        client.GetStream(),
                        targetHost,
                        targetPort,
                        proxy.Username,
                        proxy.Password,
                        timeout.Token);
                    break;
                case OutboundProxyProtocols.Socks5:
                    await EstablishSocks5TunnelAsync(
                        client.GetStream(),
                        targetHost,
                        targetPort,
                        proxy.Username,
                        proxy.Password,
                        timeout.Token);
                    break;
                default:
                    throw new NotSupportedException($"代理协议 {proxy.Protocol} 不支持 TCP 连接");
            }

            return client;
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private static async Task EstablishHttpTunnelAsync(
        NetworkStream stream,
        string targetHost,
        int targetPort,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var authority = FormatAuthority(targetHost, targetPort);
        var request = new StringBuilder()
            .Append("CONNECT ").Append(authority).Append(" HTTP/1.1\r\n")
            .Append("Host: ").Append(authority).Append("\r\n")
            .Append("Proxy-Connection: Keep-Alive\r\n");

        if (!string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password))
        {
            var token = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{username ?? string.Empty}:{password ?? string.Empty}"));
            request.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }

        request.Append("\r\n");
        var bytes = Encoding.ASCII.GetBytes(request.ToString());
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);

        var header = await ReadHttpHeaderAsync(stream, cancellationToken);
        var firstLineEnd = header.IndexOf("\r\n", StringComparison.Ordinal);
        var firstLine = firstLineEnd >= 0 ? header[..firstLineEnd] : header;
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !int.TryParse(parts[1], out var statusCode))
            throw new IOException("HTTP 代理返回了无法解析的响应");

        if (statusCode is < 200 or >= 300)
            throw new IOException($"HTTP 代理 CONNECT 失败：HTTP {statusCode}");
    }

    private static async Task<string> ReadHttpHeaderAsync(
        NetworkStream stream,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1];
        var bytes = new List<byte>(256);
        var matched = 0;
        while (bytes.Count < MaxHttpHeaderBytes)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                throw new IOException("HTTP 代理在 CONNECT 完成前关闭连接");

            var value = buffer[0];
            bytes.Add(value);
            matched = value == HttpHeaderTerminator[matched]
                ? matched + 1
                : value == HttpHeaderTerminator[0] ? 1 : 0;
            if (matched == HttpHeaderTerminator.Length)
                return Encoding.ASCII.GetString(bytes.ToArray());
        }

        throw new IOException("HTTP 代理响应头超过 32 KiB");
    }

    private static async Task EstablishSocks5TunnelAsync(
        NetworkStream stream,
        string targetHost,
        int targetPort,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var hasCredentials = !string.IsNullOrEmpty(username) || !string.IsNullOrEmpty(password);
        var methods = hasCredentials
            ? new byte[] { 0x05, 0x02, 0x00, 0x02 }
            : new byte[] { 0x05, 0x01, 0x00 };
        await stream.WriteAsync(methods, cancellationToken);

        var negotiation = await ReadExactlyAsync(stream, 2, cancellationToken);
        if (negotiation[0] != 0x05 || negotiation[1] == 0xff)
            throw new IOException("SOCKS5 代理拒绝协商");

        if (negotiation[1] == 0x02)
            await AuthenticateSocks5Async(stream, username, password, cancellationToken);
        else if (negotiation[1] != 0x00)
            throw new IOException("SOCKS5 代理选择了不支持的认证方式");

        var request = new List<byte> { 0x05, 0x01, 0x00 };
        if (IPAddress.TryParse(targetHost, out var ip))
        {
            var addressBytes = ip.GetAddressBytes();
            request.Add(addressBytes.Length == 4 ? (byte)0x01 : (byte)0x04);
            request.AddRange(addressBytes);
        }
        else
        {
            var hostBytes = Encoding.UTF8.GetBytes(targetHost);
            if (hostBytes.Length is 0 or > 255)
                throw new IOException("SOCKS5 目标主机名长度无效");

            request.Add(0x03);
            request.Add((byte)hostBytes.Length);
            request.AddRange(hostBytes);
        }

        request.Add((byte)(targetPort >> 8));
        request.Add((byte)(targetPort & 0xff));
        await stream.WriteAsync(request.ToArray(), cancellationToken);

        var response = await ReadExactlyAsync(stream, 4, cancellationToken);
        if (response[0] != 0x05 || response[1] != 0x00)
            throw new IOException($"SOCKS5 CONNECT 失败：响应码 {response[1]}");

        var addressLength = response[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => (await ReadExactlyAsync(stream, 1, cancellationToken))[0],
            _ => throw new IOException("SOCKS5 返回了未知地址类型")
        };
        _ = await ReadExactlyAsync(stream, addressLength + 2, cancellationToken);
    }

    private static async Task AuthenticateSocks5Async(
        NetworkStream stream,
        string? username,
        string? password,
        CancellationToken cancellationToken)
    {
        var userBytes = Encoding.UTF8.GetBytes(username ?? string.Empty);
        var passwordBytes = Encoding.UTF8.GetBytes(password ?? string.Empty);
        if (userBytes.Length > 255 || passwordBytes.Length > 255)
            throw new IOException("SOCKS5 用户名或密码超过 255 字节");

        var request = new byte[3 + userBytes.Length + passwordBytes.Length];
        request[0] = 0x01;
        request[1] = (byte)userBytes.Length;
        userBytes.CopyTo(request, 2);
        request[2 + userBytes.Length] = (byte)passwordBytes.Length;
        passwordBytes.CopyTo(request, 3 + userBytes.Length);
        await stream.WriteAsync(request, cancellationToken);

        var response = await ReadExactlyAsync(stream, 2, cancellationToken);
        if (response[0] != 0x01 || response[1] != 0x00)
            throw new IOException("SOCKS5 用户名密码认证失败");
    }

    private static async Task<byte[]> ReadExactlyAsync(
        Stream stream,
        int length,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, length - offset), cancellationToken);
            if (read == 0)
                throw new IOException("代理连接提前关闭");
            offset += read;
        }

        return buffer;
    }

    private static string FormatAuthority(string host, int port) =>
        host.Contains(':') && !host.StartsWith("[", StringComparison.Ordinal)
            ? $"[{host}]:{port}"
            : $"{host}:{port}";

    private static string NormalizeTargetHost(string targetHost)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetHost);
        var host = targetHost;

        if (host.StartsWith("[", StringComparison.Ordinal)
            || host.EndsWith("]", StringComparison.Ordinal))
        {
            if (!(host.StartsWith("[", StringComparison.Ordinal)
                  && host.EndsWith("]", StringComparison.Ordinal)))
            {
                throw new ArgumentException("代理目标主机格式无效", nameof(targetHost));
            }

            host = host[1..^1];
        }

        if (IPAddress.TryParse(host, out var ipAddress))
            return ipAddress.ToString();

        try
        {
            host = new IdnMapping().GetAscii(host);
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException("代理目标主机格式无效", nameof(targetHost), ex);
        }

        // 目标地址可能来自用户导入的 Session。严格按 DNS 主机名校验，避免把
        // CR/LF、空白或 URI 分隔符写入 HTTP CONNECT 请求行与 Host 头。
        if (host.Length > 253 || Uri.CheckHostName(host) != UriHostNameType.Dns)
            throw new ArgumentException("代理目标主机格式无效", nameof(targetHost));

        return host;
    }
}
