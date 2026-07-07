using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace TelegramPanel.Web.Services;

public sealed class AppRestartService
{
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<AppRestartService> _logger;
    private int _requested;

    public AppRestartService(IHostApplicationLifetime lifetime, ILogger<AppRestartService> logger)
    {
        _lifetime = lifetime;
        _logger = logger;
    }

    public bool RestartPending => Volatile.Read(ref _requested) != 0;

    public bool RequestRestart(TimeSpan? delay = null, string? reason = null)
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0)
            return false;

        delay ??= TimeSpan.FromSeconds(1);
        _logger.LogWarning("Restart requested: {Reason}. Stop in {DelayMs}ms", reason ?? "-", (int)delay.Value.TotalMilliseconds);

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(delay.Value);
            }
            catch
            {
                // ignore
            }

            _lifetime.StopApplication();
        });

        return true;
    }
}
