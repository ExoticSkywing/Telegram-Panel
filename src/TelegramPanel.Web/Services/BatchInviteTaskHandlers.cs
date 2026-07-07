using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TelegramPanel.Core.BatchTasks;
using TelegramPanel.Core.Interfaces;
using TelegramPanel.Core.Services;
using TelegramPanel.Core.Services.Telegram;
using TelegramPanel.Data.Entities;
using TelegramPanel.Modules;

namespace TelegramPanel.Web.Services;

public sealed class ChannelInviteUsersTaskHandler : IModuleTaskHandler
{
    public string TaskType => BatchTaskTypes.ChannelInviteUsers;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var channelManagement = host.Services.GetRequiredService<ChannelManagementService>();
        var channelService = host.Services.GetRequiredService<IChannelService>();
        var logger = host.Services.GetRequiredService<ILogger<ChannelInviteUsersTaskHandler>>();
        var executor = new BatchInviteTaskExecutor(
            host,
            taskManagement,
            logger,
            "频道",
            accountManagement,
            async (target, config, token) =>
            {
                var channel = await channelManagement.GetChannelAsync(target.Id);
                if (channel == null)
                    return (null, "频道不存在");

                return config.AccountId > 0
                    ? (config.AccountId, null)
                    : (await channelManagement.ResolveExecuteAccountIdAsync(channel), null);
            },
            (accountId, target, username) => channelService.InviteUserAsync(accountId, target.TelegramId, username));

        await executor.ExecuteAsync(cancellationToken);
    }
}

public sealed class GroupInviteUsersTaskHandler : IModuleTaskHandler
{
    public string TaskType => BatchTaskTypes.GroupInviteUsers;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var groupManagement = host.Services.GetRequiredService<GroupManagementService>();
        var groupService = host.Services.GetRequiredService<IGroupService>();
        var logger = host.Services.GetRequiredService<ILogger<GroupInviteUsersTaskHandler>>();
        var executor = new BatchInviteTaskExecutor(
            host,
            taskManagement,
            logger,
            "群组",
            accountManagement,
            async (target, config, token) =>
            {
                var group = await groupManagement.GetGroupAsync(target.Id);
                if (group == null)
                    return (null, "群组不存在");

                return config.AccountId > 0
                    ? (config.AccountId, null)
                    : (await groupManagement.ResolveExecuteAccountIdAsync(group), null);
            },
            (accountId, target, username) => groupService.InviteUserAsync(accountId, target.TelegramId, username));

        await executor.ExecuteAsync(cancellationToken);
    }
}

public sealed class BotChannelInviteUsersTaskHandler : IModuleTaskHandler
{
    public string TaskType => BatchTaskTypes.BotChannelInviteUsers;

    public async Task ExecuteAsync(IModuleTaskExecutionHost host, CancellationToken cancellationToken)
    {
        var taskManagement = host.Services.GetRequiredService<BatchTaskManagementService>();
        var accountManagement = host.Services.GetRequiredService<AccountManagementService>();
        var botTelegram = host.Services.GetRequiredService<BotTelegramService>();
        var channelService = host.Services.GetRequiredService<IChannelService>();
        var logger = host.Services.GetRequiredService<ILogger<BotChannelInviteUsersTaskHandler>>();

        var allAccounts = (await accountManagement.GetActiveAccountsAsync())
            .Where(x => x.UserId > 0 && x.Category?.ExcludeFromOperations != true)
            .ToList();
        var accountsByUserId = allAccounts
            .GroupBy(x => x.UserId)
            .ToDictionary(x => x.Key, x => x.First());
        var selectedAccount = allAccounts.FirstOrDefault(x => x.Id == DeserializeConfig(host.Config).SelectedAccountId);

        var executor = new BatchInviteTaskExecutor(
            host,
            taskManagement,
            logger,
            "Bot 频道",
            accountManagement,
            async (target, config, token) =>
            {
                if (config.BotId <= 0)
                    return (null, "机器人无效");

                var selected = config.SelectedAccountId > 0
                    ? selectedAccount ?? await accountManagement.GetAccountAsync(config.SelectedAccountId)
                    : null;

                return await ResolveExecutorAsync(
                    config.BotId,
                    target.TelegramId,
                    config.SelectedAccountId,
                    selected,
                    accountsByUserId,
                    botTelegram,
                    token);
            },
            (accountId, target, username) => channelService.InviteUserAsync(accountId, target.TelegramId, username));

        await executor.ExecuteAsync(cancellationToken);
    }

    private static async Task<(int? ExecutorId, string? Reason)> ResolveExecutorAsync(
        int botId,
        long channelTelegramId,
        int selectedAccountId,
        Account? selectedAccount,
        IReadOnlyDictionary<long, Account> accountsByUserId,
        BotTelegramService botTelegram,
        CancellationToken cancellationToken)
    {
        List<BotTelegramService.BotChatAdminInfo> admins;
        try
        {
            admins = await botTelegram.GetChatAdminsAsync(botId, channelTelegramId, cancellationToken);
        }
        catch (Exception ex)
        {
            return (null, $"无法获取频道管理员列表：{ex.Message}");
        }

        if (admins.Count == 0)
            return (null, "无法获取频道管理员列表（请确认 Bot 已加入且为管理员）");

        if (selectedAccountId > 0)
        {
            if (selectedAccount == null || selectedAccount.UserId <= 0 || !selectedAccount.IsActive || selectedAccount.Category?.ExcludeFromOperations == true)
                return (null, "所选执行账号无效或不可用于批量操作");

            var admin = admins.FirstOrDefault(x => x.UserId == selectedAccount.UserId);
            if (admin == null)
                return (null, "所选执行账号不是该频道管理员");

            if (!admin.IsCreator && !admin.CanInviteUsers)
                return (null, "所选执行账号缺少“邀请用户”权限");

            return (selectedAccount.Id, null);
        }

        var creator = admins.FirstOrDefault(x => x.IsCreator);
        if (creator != null && accountsByUserId.TryGetValue(creator.UserId, out var creatorAccount))
            return (creatorAccount.Id, null);

        foreach (var admin in admins)
        {
            if (!admin.IsCreator && !admin.CanInviteUsers)
                continue;

            if (accountsByUserId.TryGetValue(admin.UserId, out var account))
                return (account.Id, null);
        }

        return (null, "无可用执行账号（需要该频道管理员且拥有“邀请用户”权限，并且在系统中存在）");
    }

    private static BatchInviteTaskConfig DeserializeConfig(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            return new BatchInviteTaskConfig();

        try
        {
            return JsonSerializer.Deserialize<BatchInviteTaskConfig>(raw) ?? new BatchInviteTaskConfig();
        }
        catch
        {
            return new BatchInviteTaskConfig();
        }
    }
}

internal sealed class BatchInviteTaskExecutor
{
    private readonly IModuleTaskExecutionHost _host;
    private readonly BatchTaskManagementService _taskManagement;
    private readonly ILogger _logger;
    private readonly string _targetLabel;
    private readonly AccountManagementService? _accountManagement;
    private readonly Func<ChatInviteTargetItem, BatchInviteTaskConfig, CancellationToken, Task<(int? ExecutorId, string? Reason)>> _resolveExecutor;
    private readonly Func<int, ChatInviteTargetItem, string, Task<InviteResult>> _inviteUser;

    public BatchInviteTaskExecutor(
        IModuleTaskExecutionHost host,
        BatchTaskManagementService taskManagement,
        ILogger logger,
        string targetLabel,
        AccountManagementService? accountManagement,
        Func<ChatInviteTargetItem, BatchInviteTaskConfig, CancellationToken, Task<(int? ExecutorId, string? Reason)>> resolveExecutor,
        Func<int, ChatInviteTargetItem, string, Task<InviteResult>> inviteUser)
    {
        _host = host;
        _taskManagement = taskManagement;
        _logger = logger;
        _targetLabel = targetLabel;
        _accountManagement = accountManagement;
        _resolveExecutor = resolveExecutor;
        _inviteUser = inviteUser;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var config = DeserializeConfig(_host.Config);
        NormalizeConfig(config);

        var completed = 0;
        var failed = 0;
        var failures = new List<BatchInviteTaskFailureItem>();
        var accountPool = await BuildAccountPoolAsync(config, cancellationToken);
        var accountPoolRequested = config.ExecuteAccountIds.Count > 0 || config.AccountCategoryId.HasValue;
        var accountCursor = 0;

        try
        {
            foreach (var target in config.Targets)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!await _host.IsStillRunningAsync(cancellationToken))
                {
                    config.Canceled = true;
                    await PersistConfigAsync(config, failures, cancellationToken);
                    return;
                }

                var hasDefaultExecutor = accountPool.Count > 0;
                var defaultExecutorId = hasDefaultExecutor ? accountPool[accountCursor % accountPool.Count] : (int?)null;
                var defaultFailureReason = hasDefaultExecutor ? null : $"该{_targetLabel}暂无可用执行账号";
                if (!hasDefaultExecutor && !accountPoolRequested)
                {
                    var resolved = await _resolveExecutor(target, config, cancellationToken);
                    defaultExecutorId = resolved.ExecutorId;
                    defaultFailureReason = resolved.Reason;
                }
                else if (!hasDefaultExecutor)
                {
                    defaultFailureReason = "执行账号池为空或账号不可用";
                }

                foreach (var username in config.Usernames)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!await _host.IsStillRunningAsync(cancellationToken))
                    {
                        config.Canceled = true;
                        await PersistConfigAsync(config, failures, cancellationToken);
                        return;
                    }

                    var executorId = hasDefaultExecutor
                        ? accountPool[accountCursor++ % accountPool.Count]
                        : defaultExecutorId;

                    if (executorId is not > 0)
                    {
                        failed++;
                        failures.Add(BuildFailure(target, username, NormalizeReason(defaultFailureReason), executorId));
                        completed++;
                        await _host.UpdateProgressAsync(completed, failed, cancellationToken);
                        await PersistConfigAsync(config, failures, cancellationToken);
                        continue;
                    }

                    try
                    {
                        var result = await _inviteUser(executorId.Value, target, username);
                        if (!result.Success)
                        {
                            failed++;
                            failures.Add(BuildFailure(target, username, NormalizeReason(result.Error), executorId));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Batch invite item failed (taskId={TaskId}, targetId={TargetId}, username={Username})", _host.TaskId, target.Id, username);
                        failed++;
                        failures.Add(BuildFailure(target, username, NormalizeReason(ex.Message), executorId));
                    }
                    finally
                    {
                        completed++;
                    }

                    await _host.UpdateProgressAsync(completed, failed, cancellationToken);
                    await PersistConfigAsync(config, failures, cancellationToken);

                    if (!await DelayAsync(config.DelayMs, cancellationToken))
                    {
                        config.Canceled = true;
                        await PersistConfigAsync(config, failures, cancellationToken);
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            config.Error = ex.Message;
            await PersistConfigAsync(config, failures, cancellationToken);
            throw;
        }

        await PersistConfigAsync(config, failures, cancellationToken);
    }

    private async Task<List<int>> BuildAccountPoolAsync(BatchInviteTaskConfig config, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (config.ExecuteAccountIds.Count == 0)
            return new List<int>();

        if (_accountManagement == null)
            return config.ExecuteAccountIds.Where(x => x > 0).Distinct().ToList();

        var requestedIds = config.ExecuteAccountIds.Where(x => x > 0).Distinct().ToHashSet();
        var accounts = (await _accountManagement.GetActiveAccountsAsync())
            .Where(x => requestedIds.Contains(x.Id) && x.Category?.ExcludeFromOperations != true)
            .OrderBy(x => x.Id)
            .Select(x => x.Id)
            .ToList();
        return accounts;
    }

    private static BatchInviteTaskConfig DeserializeConfig(string? rawConfig)
    {
        var raw = (rawConfig ?? string.Empty).Trim();
        if (raw.Length == 0)
            throw new InvalidOperationException("任务缺少 Config");

        try
        {
            return JsonSerializer.Deserialize<BatchInviteTaskConfig>(raw)
                   ?? throw new InvalidOperationException("任务 Config JSON 为空");
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"任务 Config JSON 无效：{ex.Message}");
        }
    }

    private static void NormalizeConfig(BatchInviteTaskConfig config)
    {
        config.DelayMs = Math.Clamp(config.DelayMs, 0, 30000);
        if (config.ExecuteAccountIds.Count == 0 && config.AccountId > 0)
            config.ExecuteAccountIds.Add(config.AccountId);
        config.Usernames = config.Usernames
            .Select(x => (x ?? string.Empty).Trim().TrimStart('@'))
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        config.Targets = config.Targets
            .Where(x => x.Id > 0 && x.TelegramId != 0)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToList();

        if (config.Targets.Count == 0)
            throw new InvalidOperationException("任务缺少目标列表");
        if (config.Usernames.Count == 0)
            throw new InvalidOperationException("任务缺少用户名列表");
    }

    private async Task<bool> DelayAsync(int delayMs, CancellationToken cancellationToken)
    {
        delayMs = Math.Clamp(delayMs, 0, 30000);
        if (delayMs <= 0)
            return true;

        var remaining = delayMs;
        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await _host.IsStillRunningAsync(cancellationToken))
                return false;

            var chunk = Math.Min(remaining, 1000);
            await Task.Delay(chunk, cancellationToken);
            remaining -= chunk;
        }

        return true;
    }

    private async Task PersistConfigAsync(
        BatchInviteTaskConfig config,
        List<BatchInviteTaskFailureItem> failures,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        config.Failures = failures.TakeLast(300).ToList();
        config.FailureLines = BatchInviteTaskFailureFormatter.BuildLines(config.Failures);
        await _taskManagement.UpdateTaskConfigAsync(_host.TaskId, JsonSerializer.Serialize(config, new JsonSerializerOptions
        {
            WriteIndented = true
        }));
    }

    private static BatchInviteTaskFailureItem BuildFailure(ChatInviteTargetItem target, string? username, string reason, int? executorAccountId = null) =>
        new()
        {
            TargetId = target.Id,
            TargetTelegramId = target.TelegramId,
            TargetTitle = NormalizeTitle(target),
            ExecutorAccountId = executorAccountId,
            Username = (username ?? string.Empty).Trim().TrimStart('@'),
            Reason = NormalizeReason(reason)
        };

    private static string NormalizeTitle(ChatInviteTargetItem target)
    {
        var title = (target.Title ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(title) ? target.TelegramId.ToString() : title;
    }

    private static string NormalizeReason(string? reason)
    {
        var text = (reason ?? string.Empty).Trim();
        return string.IsNullOrWhiteSpace(text) ? "失败" : text;
    }
}
