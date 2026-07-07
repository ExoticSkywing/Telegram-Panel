using Microsoft.Extensions.Options;

namespace TelegramPanel.Web.Services;

public sealed class PanelTimeZoneService
{
    private readonly object _gate = new();
    private TimeZoneInfo _timeZone = TimeZoneInfo.Utc;

    public PanelTimeZoneService(IOptionsMonitor<PanelTimeZoneOptions> optionsMonitor)
    {
        Apply(optionsMonitor.CurrentValue);
        optionsMonitor.OnChange(Apply);
    }

    public TimeZoneInfo Current
    {
        get
        {
            lock (_gate)
            {
                return _timeZone;
            }
        }
    }

    public TimeZoneInfo ApplyTimeZoneId(string? timeZoneId)
    {
        var tz = Resolve((timeZoneId ?? string.Empty).Trim());
        lock (_gate)
        {
            _timeZone = tz;
        }

        return tz;
    }

    public string Format(DateTime? valueUtcOrUnspecified, string format = "yyyy-MM-dd HH:mm", string emptyText = "-")
    {
        if (valueUtcOrUnspecified == null)
            return emptyText;

        try
        {
            var converted = ConvertFromUtcOrUnspecified(valueUtcOrUnspecified.Value);
            return converted.ToString(format);
        }
        catch
        {
            // 数据库里的历史/异常时间值不应导致整个 Blazor 电路中断。
            return emptyText;
        }
    }

    public DateTime ConvertFromUtcOrUnspecified(DateTime valueUtcOrUnspecified)
    {
        DateTime utc;
        try
        {
            utc = valueUtcOrUnspecified.Kind switch
            {
                DateTimeKind.Utc => valueUtcOrUnspecified,
                DateTimeKind.Local => valueUtcOrUnspecified.ToUniversalTime(),
                _ => DateTime.SpecifyKind(valueUtcOrUnspecified, DateTimeKind.Utc)
            };
        }
        catch (ArgumentOutOfRangeException)
        {
            return valueUtcOrUnspecified;
        }

        var tz = Current;
        try
        {
            return TimeZoneInfo.ConvertTimeFromUtc(utc, tz);
        }
        catch (ArgumentOutOfRangeException)
        {
            return utc;
        }
        catch (ArgumentException)
        {
            return utc;
        }
    }

    private void Apply(PanelTimeZoneOptions options)
    {
        ApplyTimeZoneId(options.TimeZoneId);
    }

    private static TimeZoneInfo Resolve(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return TimeZoneInfo.Utc;

        if (TryFind(timeZoneId, out var tz))
            return tz;

        // 常见跨平台兜底：IANA <-> Windows
        if (string.Equals(timeZoneId, "Asia/Shanghai", StringComparison.OrdinalIgnoreCase)
            && TryFind("China Standard Time", out tz))
            return tz;

        if (string.Equals(timeZoneId, "China Standard Time", StringComparison.OrdinalIgnoreCase)
            && TryFind("Asia/Shanghai", out tz))
            return tz;

        if (string.Equals(timeZoneId, "UTC", StringComparison.OrdinalIgnoreCase) && TryFind("Etc/UTC", out tz))
            return tz;

        if (string.Equals(timeZoneId, "Etc/UTC", StringComparison.OrdinalIgnoreCase) && TryFind("UTC", out tz))
            return tz;

        return TimeZoneInfo.Utc;
    }

    private static bool TryFind(string id, out TimeZoneInfo timeZone)
    {
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(id);
            return true;
        }
        catch
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }
    }
}
