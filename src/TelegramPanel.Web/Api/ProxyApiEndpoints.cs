using TelegramPanel.Core.Models;
using TelegramPanel.Core.Services.Proxy;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TelegramPanel.Web.Services;

namespace TelegramPanel.Web.Api;

public static class ProxyApiEndpoints
{
    public static RouteGroupBuilder MapProxyManagementApi(this RouteGroupBuilder group)
    {
        group.MapGet("/network/egress", ProbePanelEgressAsync);

        group.MapGet("/proxies", ListAsync);
        group.MapGet("/proxies/{id:int}", GetAsync);
        group.MapPost("/proxies", CreateAsync);
        group.MapPut("/proxies/{id:int}", UpdateAsync);
        group.MapDelete("/proxies/{id:int}", DeleteAsync);
        group.MapPost("/proxies/{id:int}/test", TestAsync);
        group.MapPost("/proxies/import", ImportAsync);
        group.MapGet("/proxies/warp/status", GetWarpStatusAsync);
        group.MapPost("/proxies/warp", CreateWarpAsync);

        group.MapPost("/accounts/{id:int}/proxy", BindAccountAsync);
        group.MapPost("/accounts/batch/proxy", BindAccountsAsync);
        group.MapGet("/accounts/{id:int}/proxy/egress", ProbeAccountEgressAsync);
        return group;
    }

    private static async Task<IResult> ProbePanelEgressAsync(
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ProbePanelAsync(cancellationToken);
        return Results.Ok(ToDto(result, "panel_direct"));
    }

    private static async Task<IResult> ListAsync(
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        var proxies = await service.ListAsync(cancellationToken);
        return Results.Ok(proxies.Select(ToDto).ToList());
    }

    private static async Task<IResult> GetAsync(
        int id,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        var proxy = await service.GetAsync(id, includeAccounts: true, cancellationToken);
        return proxy == null
            ? Results.NotFound(new OperationResultDto(false, "代理不存在"))
            : Results.Ok(ToDto(proxy));
    }

    private static async Task<IResult> CreateAsync(
        ProxySaveRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var proxy = await service.CreateAsync(ToInput(request), cancellationToken);
            return Results.Ok(ToDto(proxy));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> UpdateAsync(
        int id,
        ProxySaveRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var proxy = await service.UpdateAsync(id, ToInput(request), cancellationToken);
            return Results.Ok(ToDto(proxy));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> DeleteAsync(
        int id,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            await service.DeleteAsync(id, cancellationToken);
            return Results.Ok(new OperationResultDto(true, "代理已删除"));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> TestAsync(
        int id,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var proxy = await service.TestAsync(id, cancellationToken);
            return Results.Ok(ToDto(proxy));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> ImportAsync(
        ProxyImportRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var proxies = await service.ImportAsync(
                request.Text,
                request.TestAfterImport,
                cancellationToken);
            return Results.Ok(proxies.Select(ToDto).ToList());
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> GetWarpStatusAsync(
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        return Results.Ok(await service.GetWarpStatusAsync(cancellationToken));
    }

    private static async Task<IResult> CreateWarpAsync(
        WarpCreateRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            if (AccountLoginProxyCoordinator.IsManagedWarpRequestId(request.RequestId)
                || AccountImportService.IsManagedWarpRequestId(request.RequestId))
                throw new ArgumentException("该 WARP 请求 ID 前缀为系统内部保留值");

            var proxy = await service.CreateWarpAsync(
                request.Name,
                request.RequestId,
                cancellationToken,
                request.Protocol);
            return Results.Ok(ToDto(proxy));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> BindAccountAsync(
        int id,
        AccountProxyBindingRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.BindAccountsAsync(
                new[] { id },
                new AccountProxyBindingInput(
                    request.Strategy ?? string.Empty,
                    request.ProxyId,
                    request.ExpectedProxyId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> BindAccountsAsync(
        BatchAccountProxyBindingRequestDto request,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.BindAccountsAsync(
                request.AccountIds ?? Array.Empty<int>(),
                new AccountProxyBindingInput(
                    request.Strategy ?? string.Empty,
                    request.ProxyId),
                cancellationToken);
            return Results.Ok(result);
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static async Task<IResult> ProbeAccountEgressAsync(
        int id,
        ProxyManagementService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await service.ProbeAccountAsync(id, cancellationToken);
            return Results.Ok(ToDto(result, "account_effective_route"));
        }
        catch (Exception ex) when (IsClientError(ex))
        {
            return ToError(ex);
        }
    }

    private static OutboundProxyInput ToInput(ProxySaveRequestDto request) =>
        new(
            request.Name,
            request.Kind,
            request.Protocol,
            request.Host,
            request.Port,
            request.Username,
            request.Password,
            request.Secret,
            request.ResinPlatform,
            request.ResinAdminUrl,
            request.ResinAdminToken,
            request.IsEnabled,
            request.TestAfterSave,
            request.ClearPassword,
            request.ClearResinAdminToken);

    public static ProxyDto ToDto(OutboundProxy proxy) =>
        new(
            proxy.Id,
            proxy.Name,
            proxy.Kind,
            proxy.Protocol,
            proxy.Host,
            proxy.Port,
            proxy.Username,
            !string.IsNullOrWhiteSpace(proxy.Password),
            !string.IsNullOrWhiteSpace(proxy.Secret),
            proxy.ResinPlatform,
            proxy.ResinAdminUrl,
            !string.IsNullOrWhiteSpace(proxy.ResinAdminToken),
            proxy.IsEnabled,
            proxy.TestStatus,
            proxy.LastError,
            proxy.LastLatencyMs,
            proxy.EgressIp,
            proxy.EgressCountry,
            proxy.EgressCity,
            proxy.EgressIsp,
            proxy.WarpProfile?.WarpStatus,
            proxy.LastTestedAtUtc,
            proxy.FirstBoundAtUtc,
            proxy.Accounts?.Count ?? 0,
            proxy.CreatedAtUtc,
            proxy.UpdatedAtUtc);

    private static EgressProbeDto ToDto(EgressProbeResult result, string source) =>
        new(
            result.Success,
            result.Ip,
            result.Country,
            result.City,
            result.Isp,
            result.WarpStatus,
            result.LatencyMs,
            result.CheckedAtUtc,
            result.Error,
            source);

    private static bool IsClientError(Exception exception) =>
        exception is ArgumentException
            or InvalidOperationException
            or KeyNotFoundException;

    private static IResult ToError(Exception exception) => exception switch
    {
        ProxyInUseException or ProxyBindingConflictException =>
            Results.Conflict(new OperationResultDto(false, exception.Message)),
        KeyNotFoundException =>
            Results.NotFound(new OperationResultDto(false, exception.Message)),
        _ => Results.BadRequest(new OperationResultDto(false, exception.Message))
    };
}

public sealed record ProxySaveRequestDto(
    string? Name,
    string? Kind,
    string? Protocol,
    string? Host,
    int Port,
    string? Username,
    string? Password,
    string? Secret,
    string? ResinPlatform,
    string? ResinAdminUrl,
    string? ResinAdminToken,
    bool IsEnabled = true,
    bool TestAfterSave = false,
    bool ClearPassword = false,
    bool ClearResinAdminToken = false);

public sealed record ProxyImportRequestDto(string? Text, bool TestAfterImport = false);
public sealed record WarpCreateRequestDto(
    string? Name,
    string? RequestId,
    string? Protocol = null);
public sealed record AccountProxyBindingRequestDto(
    string? Strategy,
    int? ProxyId,
    int? ExpectedProxyId);
public sealed record BatchAccountProxyBindingRequestDto(
    IReadOnlyList<int>? AccountIds,
    string? Strategy,
    int? ProxyId);

public sealed record ProxyDto(
    int Id,
    string Name,
    string Kind,
    string Protocol,
    string Host,
    int Port,
    string? Username,
    bool HasPassword,
    bool HasSecret,
    string? ResinPlatform,
    string? ResinAdminUrl,
    bool HasResinAdminToken,
    bool IsEnabled,
    string TestStatus,
    string? LastError,
    int? LastLatencyMs,
    string? EgressIp,
    string? EgressCountry,
    string? EgressCity,
    string? EgressIsp,
    string? WarpStatus,
    DateTime? LastTestedAtUtc,
    DateTime? FirstBoundAtUtc,
    int AccountCount,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);

public sealed record EgressProbeDto(
    bool Success,
    string? Ip,
    string? Country,
    string? City,
    string? Isp,
    string? WarpStatus,
    int? LatencyMs,
    DateTime CheckedAtUtc,
    string? Error,
    string Source);
