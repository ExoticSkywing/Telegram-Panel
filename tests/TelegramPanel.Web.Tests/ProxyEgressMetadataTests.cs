using System.Net;
using TelegramPanel.Core.Services.Proxy;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class ProxyEgressMetadataTests
{
    [Fact]
    public void 出口地理信息包含国家地区城市和ISP()
    {
        const string json = """
                            {
                              "ip": "8.8.8.8",
                              "success": true,
                              "country": "United States",
                              "country_code": "US",
                              "region": "California",
                              "city": "Mountain View",
                              "isp": "Google LLC"
                            }
                            """;

        var metadata = ProxyEgressProbeService.ParseGeoMetadata(
            json,
            IPAddress.Parse("8.8.8.8"));

        Assert.NotNull(metadata);
        Assert.Equal("United States", metadata.Country);
        Assert.Equal("California · Mountain View", metadata.Location);
        Assert.Equal("Google LLC", metadata.Isp);
    }

    [Fact]
    public void 地理服务返回不同IP时拒绝采用结果()
    {
        const string json = """
                            {
                              "ip": "1.1.1.1",
                              "success": true,
                              "country": "Australia",
                              "city": "Sydney",
                              "isp": "Cloudflare"
                            }
                            """;

        var metadata = ProxyEgressProbeService.ParseGeoMetadata(
            json,
            IPAddress.Parse("8.8.8.8"));

        Assert.Null(metadata);
    }

    [Fact]
    public void 地理服务失败结果不会污染已有出口IP()
    {
        const string json = """
                            {
                              "ip": "8.8.8.8",
                              "success": false,
                              "message": "rate limited"
                            }
                            """;

        var metadata = ProxyEgressProbeService.ParseGeoMetadata(
            json,
            IPAddress.Parse("8.8.8.8"));

        Assert.Null(metadata);
    }

    [Fact]
    public void 地理缓存超过上限时会淘汰最旧条目()
    {
        var prefix = $"cache-limit-{Guid.NewGuid():N}-";
        var start = DateTime.UtcNow.AddDays(-1);
        for (var index = 0;
             index < ProxyEgressProbeService.MaxGeoCacheEntries + 64;
             index++)
        {
            ProxyEgressProbeService.CacheGeoMetadata(
                $"{prefix}{index}",
                metadata: null,
                start.AddTicks(index));
        }

        Assert.InRange(
            ProxyEgressProbeService.GeoCacheEntryCount,
            1,
            ProxyEgressProbeService.MaxGeoCacheEntries);
    }
}
