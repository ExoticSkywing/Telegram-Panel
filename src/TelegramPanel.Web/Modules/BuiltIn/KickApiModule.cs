using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using System.Text.Json;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Core.Services;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;
using TelegramPanel.Web.ExternalApi;

namespace TelegramPanel.Web.Modules.BuiltIn;

public sealed class KickApiModule : ITelegramPanelModule, IModuleApiProvider, IModuleUiProvider
{
    public KickApiModule(string version)
    {
        Manifest = new ModuleManifest
        {
            Id = "builtin.kick-api",
            Name = "外部 API：踢人/封禁",
            Version = version,
            Host = new HostCompatibility(),
            Entry = new ModuleEntryPoint { Assembly = "", Type = typeof(KickApiModule).FullName ?? "" }
        };
    }

    public ModuleManifest Manifest { get; }

    public void ConfigureServices(IServiceCollection services, ModuleHostContext context)
    {
        services.AddScoped<IModuleTaskHandler, ExternalApiKickTaskHandler>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints, ModuleHostContext context)
    {
        if (!endpoints.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("ExternalApi:Enabled", true))
        {
            // 预留开关；默认开启
        }

        endpoints.MapKickApi();

        var authEnabled = endpoints.ServiceProvider.GetRequiredService<IConfiguration>().GetValue("AdminAuth:Enabled", true);
        var page = endpoints.MapGet("/ext/builtin.kick-api/kick", GetSettingsPageAsync);
        if (authEnabled)
            page.RequireAuthorization();

        endpoints.MapGet("/ext/builtin.kick-api/assets/{file}", GetAssetAsync);

        var api = endpoints.MapGroup("/api/panel/extensions/kick-api");
        if (authEnabled)
            api.RequireAuthorization();

        api.MapGet("", GetStateAsync);
        api.MapGet("/bots/{botId:int}/chats", GetBotChatsAsync);
        api.MapPost("/tasks", CreateKickTasksAsync);
    }

    public IEnumerable<ModuleApiTypeDefinition> GetApis(ModuleHostContext context)
    {
        yield return new ModuleApiTypeDefinition
        {
            Type = ExternalApiTypes.Kick,
            DisplayName = "踢人/封禁",
            Route = "/api/kick",
            Description = "从配置的 Bot 管理的频道/群组中踢出或封禁指定用户（按 X-API-Key 匹配配置项）。",
            Order = 10
        };
    }

    public IEnumerable<ModuleNavItem> GetNavItems(ModuleHostContext context)
    {
        yield return new ModuleNavItem
        {
            Title = "踢人/封禁",
            Href = "/ext/builtin.kick-api/kick",
            Icon = Icons.Material.Filled.PersonRemove,
            Group = "外部 API",
            Order = 10
        };
    }

    public IEnumerable<ModulePageDefinition> GetPages(ModuleHostContext context)
        => Array.Empty<ModulePageDefinition>();

    private static IResult GetSettingsPageAsync()
        => Results.Content(KickApiStaticPage.Html, "text/html; charset=utf-8");

    private static async Task<IResult> GetAssetAsync(string file, HttpContext http)
    {
        file = Path.GetFileName((file ?? string.Empty).Trim());
        if (file.Length == 0)
            return Results.NotFound();

        var path = Path.Combine(AppContext.BaseDirectory, "wwwroot", "builtin-kick-api", "vendor", file);
        if (!File.Exists(path))
            return Results.NotFound();

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        var bytes = await File.ReadAllBytesAsync(path, http.RequestAborted);
        return Results.File(bytes, "text/javascript; charset=utf-8");
    }

    private static async Task<IResult> GetStateAsync(BotManagementService botManagement)
    {
        var bots = (await botManagement.GetAllBotsAsync())
            .Where(x => x.IsActive)
            .OrderBy(x => x.Id)
            .Select(x => new KickBotDto(x.Id, x.Name, x.Username))
            .ToList();

        var categories = (await botManagement.GetCategoriesAsync())
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => new KickCategoryDto(x.Id, x.Name, x.Description))
            .ToList();

        return Results.Ok(new KickPageDto(bots, categories));
    }

    private static async Task<IResult> GetBotChatsAsync(int botId, BotManagementService botManagement)
    {
        if (botId <= 0)
            return Results.Ok(Array.Empty<KickChatDto>());

        var categories = (await botManagement.GetCategoriesAsync()).ToList();
        var categoryNames = categories.ToDictionary(x => x.Id, x => x.Name);
        var chats = (await botManagement.GetChatsAsync(botId))
            .OrderBy(x => GetCategoryName(x, categoryNames))
            .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
            .Select(x => ToChatDto(x, categoryNames))
            .ToList();
        return Results.Ok(chats);
    }

    private static async Task<IResult> CreateKickTasksAsync(
        KickPageCreateTaskRequest request,
        BotManagementService botManagement,
        BatchTaskManagementService taskManagement)
    {
        var userIds = (request.UserIds ?? Array.Empty<long>())
            .Where(x => x > 0)
            .Distinct()
            .OrderBy(x => x)
            .ToList();
        if (userIds.Count == 0)
            return Results.BadRequest(new KickPageOperationResult(false, "请至少填写一个用户 ID", Array.Empty<int>()));

        var botId = request.BotId;
        var useAllChats = botId == 0 || request.UseAllChats;
        var targetChatIds = new List<long>();
        var total = 0;

        if (botId == 0)
        {
            var bots = (await botManagement.GetAllBotsAsync()).Where(x => x.IsActive).ToList();
            foreach (var bot in bots)
                total += (await botManagement.GetChatsAsync(bot.Id)).Count();
        }
        else
        {
            var chats = (await botManagement.GetChatsAsync(botId)).ToList();
            if (useAllChats)
            {
                total = chats.Count;
            }
            else
            {
                var selectedIds = new HashSet<long>((request.ChatIds ?? Array.Empty<long>()).Where(x => x != 0));
                var selectedCategoryIds = new HashSet<int>((request.CategoryIds ?? Array.Empty<int>()).Where(x => x > 0));
                if (selectedCategoryIds.Count > 0 || request.IncludeUncategorized)
                {
                    foreach (var chat in chats)
                    {
                        if (chat.CategoryId.HasValue)
                        {
                            if (selectedCategoryIds.Contains(chat.CategoryId.Value))
                                selectedIds.Add(chat.TelegramId);
                        }
                        else if (request.IncludeUncategorized)
                        {
                            selectedIds.Add(chat.TelegramId);
                        }
                    }
                }

                var knownIds = chats.Select(x => x.TelegramId).ToHashSet();
                targetChatIds = selectedIds.Where(knownIds.Contains).OrderBy(x => x).ToList();
                total = targetChatIds.Count;
            }
        }

        if (total <= 0)
            return Results.BadRequest(new KickPageOperationResult(false, "未匹配到任何可操作的频道/群组", Array.Empty<int>()));

        var createdIds = new List<int>();
        foreach (var userId in userIds)
        {
            var log = new KickTaskLog
            {
                ApiName = $"模块页面 (user={userId})",
                BotId = botId,
                UseAllChats = useAllChats,
                ChatIds = useAllChats ? new List<long>() : targetChatIds,
                UserId = userId,
                PermanentBan = request.PermanentBan,
                RequestedAtUtc = DateTime.UtcNow
            };

            var task = await taskManagement.CreateTaskAsync(new BatchTask
            {
                TaskType = BatchTaskTypes.ExternalApiKick,
                Total = total,
                Completed = 0,
                Failed = 0,
                Config = JsonSerializer.Serialize(log, new JsonSerializerOptions { WriteIndented = true })
            });
            createdIds.Add(task.Id);
        }

        return Results.Ok(new KickPageOperationResult(true, $"已提交 {createdIds.Count} 个任务", createdIds));
    }

    private static KickChatDto ToChatDto(BotChannel chat, IReadOnlyDictionary<int, string> categoryNames)
    {
        var categoryName = GetCategoryName(chat, categoryNames);
        var label = string.IsNullOrWhiteSpace(chat.Username)
            ? $"{chat.Title} ({chat.TelegramId})"
            : $"{chat.Title} (@{chat.Username})";
        return new KickChatDto(
            chat.Id,
            chat.TelegramId,
            label,
            chat.IsBroadcast,
            chat.CategoryId,
            categoryName,
            $"{label} {categoryName} {chat.TelegramId}".ToLowerInvariant());
    }

    private static string GetCategoryName(BotChannel chat, IReadOnlyDictionary<int, string> categoryNames)
        => chat.CategoryId.HasValue && categoryNames.TryGetValue(chat.CategoryId.Value, out var name) ? name : "未分类";
}

internal sealed record KickPageDto(IReadOnlyList<KickBotDto> Bots, IReadOnlyList<KickCategoryDto> Categories);
internal sealed record KickBotDto(int Id, string Name, string? Username);
internal sealed record KickCategoryDto(int Id, string Name, string? Description);
internal sealed record KickChatDto(int Id, long TelegramId, string Label, bool IsBroadcast, int? CategoryId, string CategoryName, string SearchText);
internal sealed record KickPageCreateTaskRequest(
    int BotId,
    IReadOnlyList<long>? UserIds,
    bool PermanentBan,
    bool UseAllChats,
    IReadOnlyList<int>? CategoryIds,
    bool IncludeUncategorized,
    IReadOnlyList<long>? ChatIds);
internal sealed record KickPageOperationResult(bool Success, string Message, IReadOnlyList<int> TaskIds);
