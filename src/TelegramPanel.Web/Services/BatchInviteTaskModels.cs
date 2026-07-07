namespace TelegramPanel.Web.Services;

public sealed class ChatInviteTargetItem
{
    public int Id { get; set; }
    public long TelegramId { get; set; }
    public string Title { get; set; } = "";
}

public sealed class BatchInviteTaskConfig
{
    public int AccountId { get; set; }
    public int BotId { get; set; }
    public int SelectedAccountId { get; set; }
    public int? AccountCategoryId { get; set; }
    public string AccountScopeName { get; set; } = "";
    public List<int> ExecuteAccountIds { get; set; } = new();
    public int DelayMs { get; set; } = 2000;
    public List<string> Usernames { get; set; } = new();
    public List<ChatInviteTargetItem> Targets { get; set; } = new();
    public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;
    public List<BatchInviteTaskFailureItem>? Failures { get; set; }
    public List<string>? FailureLines { get; set; }
    public bool Canceled { get; set; }
    public string? Error { get; set; }
}

public sealed class BatchInviteTaskFailureItem
{
    public int TargetId { get; set; }
    public long TargetTelegramId { get; set; }
    public string TargetTitle { get; set; } = "";
    public int? ExecutorAccountId { get; set; }
    public string? Username { get; set; }
    public string Reason { get; set; } = "";
}

public static class BatchInviteTaskFailureFormatter
{
    public static List<string> BuildLines(IReadOnlyList<BatchInviteTaskFailureItem> failures)
    {
        if (failures.Count == 0)
            return new List<string>();

        static string NormalizeText(string? value, string fallback)
        {
            var text = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(text) ? fallback : text;
        }

        return failures
            .GroupBy(x => new
            {
                x.TargetId,
                x.TargetTelegramId,
                Title = NormalizeText(x.TargetTitle, "未命名目标")
            })
            .OrderBy(x => x.Key.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Key.TargetId)
            .Select(group =>
            {
                var targetLabel = group.Key.TargetTelegramId == 0
                    ? group.Key.Title
                    : $"{group.Key.Title}（{group.Key.TargetTelegramId}）";
                var userSegments = group
                    .Where(x => !string.IsNullOrWhiteSpace(x.Username))
                    .GroupBy(x => "@" + x.Username!.Trim().TrimStart('@'), StringComparer.OrdinalIgnoreCase)
                    .Select(x =>
                    {
                        var reasons = x.Select(y => NormalizeText(y.Reason, "失败"))
                            .Distinct(StringComparer.Ordinal);
                        return $"{x.Key}（{string.Join(" / ", reasons)}）";
                    })
                    .ToList();
                var targetReasons = group
                    .Where(x => string.IsNullOrWhiteSpace(x.Username))
                    .Select(x => NormalizeText(x.Reason, "失败"))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                var segments = new List<string>();
                if (targetReasons.Count > 0)
                    segments.Add(string.Join(" / ", targetReasons));
                if (userSegments.Count > 0)
                    segments.Add($"用户失败：{string.Join("；", userSegments)}");
                return $"{targetLabel}：{(segments.Count == 0 ? "失败" : string.Join("；", segments))}";
            })
            .ToList();
    }
}
