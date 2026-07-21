using Microsoft.Extensions.Configuration;
using TelegramPanel.Core.Models;

namespace TelegramPanel.Core.Services.Proxy;

/// <summary>
/// 将 Telegram 全局代理配置转换为统一的运行时连接参数。
/// </summary>
public static class GlobalTelegramProxyConfiguration
{
    public const string ManualSourceMode = "manual";
    public const string ExistingSourceMode = "existing";

    public static string GetSourceMode(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = (configuration["Telegram:Proxy:SourceMode"] ?? string.Empty)
            .Trim()
            .ToLowerInvariant();
        return string.IsNullOrWhiteSpace(value) ? ManualSourceMode : value;
    }

    public static int? GetSelectedProxyId(
        IConfiguration configuration,
        bool requireEnabled = true)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (requireEnabled && !IsEnabled(configuration))
            return null;
        if (GetSourceMode(configuration) != ExistingSourceMode)
            return null;

        return int.TryParse(configuration["Telegram:Proxy:ProxyId"], out var id) && id > 0
            ? id
            : null;
    }

    public static bool IsEnabled(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var enabledText = (configuration["Telegram:Proxy:Enabled"] ?? string.Empty).Trim();
        if (bool.TryParse(enabledText, out var enabled))
            return enabled;

        if (GetSourceMode(configuration) == ExistingSourceMode)
            return GetSelectedProxyId(configuration, requireEnabled: false).HasValue;

        return !string.IsNullOrWhiteSpace(configuration["Telegram:Proxy:Server"])
               || !string.IsNullOrWhiteSpace(configuration["Telegram:Proxy:Port"]);
    }

    /// <summary>
    /// 解析必须存在的 Telegram 全局代理。选择全局代理属于明确的出站路由，
    /// 配置缺失时绝不能静默降级为面板直连。
    /// </summary>
    public static ProxyConnectionOptions BuildRequired(IConfiguration configuration) =>
        Build(configuration)
        ?? throw new InvalidOperationException(
            "Telegram 全局代理尚未配置，已阻止降级为直连");

    public static ProxyConnectionOptions? Build(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        if (GetSourceMode(configuration) == ExistingSourceMode)
        {
            if (!IsEnabled(configuration))
                return null;
            throw new InvalidOperationException(
                "Telegram 全局代理引用已有代理，必须通过数据库解析，已阻止使用过期配置或降级为直连");
        }

        var enabledText = (configuration["Telegram:Proxy:Enabled"] ?? string.Empty).Trim();
        bool? explicitlyEnabled = bool.TryParse(enabledText, out var enabled) ? enabled : null;
        if (explicitlyEnabled == false)
            return null;

        var host = (configuration["Telegram:Proxy:Server"] ?? string.Empty)
            .Trim()
            .Trim('[', ']');
        var portText = (configuration["Telegram:Proxy:Port"] ?? string.Empty).Trim();
        if (explicitlyEnabled != true
            && string.IsNullOrWhiteSpace(host)
            && string.IsNullOrWhiteSpace(portText))
            return null;
        if (string.IsNullOrWhiteSpace(host)
            || !int.TryParse(portText, out var port)
            || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("Telegram 全局代理地址或端口配置无效");
        }

        var secret = NormalizeOptional(configuration["Telegram:Proxy:Secret"]);
        var configuredProtocol = NormalizeOptional(configuration["Telegram:Proxy:Protocol"])
            ?.ToLowerInvariant();
        var protocol = configuredProtocol
                       ?? (secret == null
                           ? OutboundProxyProtocols.Socks5
                           : OutboundProxyProtocols.MtProto);
        if (!OutboundProxyProtocols.IsSupported(protocol))
            throw new InvalidOperationException("Telegram 全局代理协议无效，仅支持 http、socks5 或 mtproto");
        if (protocol == OutboundProxyProtocols.MtProto && secret == null)
            throw new InvalidOperationException("Telegram 全局 MTProxy 缺少 Secret");

        return new ProxyConnectionOptions(
            0,
            "Telegram 全局代理",
            OutboundProxyKinds.Manual,
            protocol,
            host,
            port,
            NormalizeOptional(configuration["Telegram:Proxy:Username"]),
            NormalizeOptional(configuration["Telegram:Proxy:Password"]),
            protocol == OutboundProxyProtocols.MtProto ? secret : null);
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
