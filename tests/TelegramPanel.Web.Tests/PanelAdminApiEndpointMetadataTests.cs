using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using TelegramPanel.Web.Api;
using Xunit;

namespace TelegramPanel.Web.Tests;

public sealed class PanelAdminApiEndpointMetadataTests
{
    [Fact]
    public async Task ZipImport_TransportLimitsAllowTheBusinessFileLimit()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing"
        });
        await using var app = builder.Build();
        PanelAdminApiEndpoints.ConfigureAccountImportZipLimits(
            app.MapPost("/zip-import", () => "ok"));

        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(item => string.Equals(
                item.RoutePattern.RawText,
                "/zip-import",
                StringComparison.Ordinal));

        var requestLimit = endpoint.Metadata.GetMetadata<IRequestSizeLimitMetadata>();
        var formLimits = endpoint.Metadata.GetMetadata<IFormOptionsMetadata>();

        Assert.NotNull(requestLimit);
        Assert.Equal(
            PanelAdminApiEndpoints.AccountImportZipMaxRequestSize,
            requestLimit.MaxRequestBodySize);
        Assert.NotNull(formLimits);
        Assert.Equal(
            PanelAdminApiEndpoints.AccountImportZipMaxRequestSize,
            formLimits.MultipartBodyLengthLimit);
    }

    [Fact]
    public void PrepareZipImportRequest_ConfiguresManualFormReadLimits()
    {
        var context = new DefaultHttpContext();
        var requestSizeFeature = new MutableRequestSizeFeature();
        context.Features.Set<IHttpMaxRequestBodySizeFeature>(
            requestSizeFeature);

        var formOptions =
            PanelAdminApiEndpoints.PrepareAccountImportZipRequest(
                context.Request);

        Assert.Equal(
            PanelAdminApiEndpoints.AccountImportZipMaxRequestSize,
            requestSizeFeature.MaxRequestBodySize);
        Assert.Equal(
            PanelAdminApiEndpoints.AccountImportZipMaxRequestSize,
            formOptions.MultipartBodyLengthLimit);
    }

    private sealed class MutableRequestSizeFeature
        : IHttpMaxRequestBodySizeFeature
    {
        public bool IsReadOnly => false;
        public long? MaxRequestBodySize { get; set; }
    }
}
