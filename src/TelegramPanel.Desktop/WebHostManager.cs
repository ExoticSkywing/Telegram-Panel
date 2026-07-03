using System.Diagnostics;
using System.Net.Http;

namespace TelegramPanel.Desktop;

internal sealed class WebHostManager : IDisposable
{
    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    private Process? _process;
    private bool _disposed;

    public Uri BaseUri { get; } = new(DesktopPaths.DefaultUrl);

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DesktopPaths.AppDataRoot);
        Directory.CreateDirectory(DesktopPaths.LogsRoot);

        if (!File.Exists(DesktopPaths.WebExecutablePath))
            throw new FileNotFoundException("未找到内置 Web 服务程序，请重新安装 Telegram Panel。", DesktopPaths.WebExecutablePath);

        if (!await IsHealthyAsync(cancellationToken))
            StartProcess();

        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await IsHealthyAsync(cancellationToken))
                return;

            if (_process is { HasExited: true })
                throw new InvalidOperationException($"Web 服务启动失败，退出码：{_process.ExitCode}");

            await Task.Delay(500, cancellationToken);
        }

        throw new TimeoutException("Web 服务启动超时，请查看日志目录中的 desktop.log。");
    }

    public void Stop()
    {
        if (_process == null || _process.HasExited)
            return;

        try
        {
            _process.CloseMainWindow();
            if (_process.WaitForExit(3000))
                return;
        }
        catch
        {
            // 关闭失败时继续强制结束进程。
        }

        try
        {
            _process.Kill(entireProcessTree: true);
        }
        catch
        {
            // 退出阶段不再打断用户流程。
        }
    }

    private void StartProcess()
    {
        var appDataRoot = DesktopPaths.AppDataRoot;
        var startInfo = new ProcessStartInfo
        {
            FileName = DesktopPaths.WebExecutablePath,
            WorkingDirectory = DesktopPaths.WebRoot,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.Environment["ASPNETCORE_URLS"] = DesktopPaths.DefaultUrl;
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        startInfo.Environment["DOTNET_ENVIRONMENT"] = "Production";
        startInfo.Environment["ConnectionStrings__DefaultConnection"] = $"Data Source={Path.Combine(appDataRoot, "telegram-panel.db")}";
        startInfo.Environment["Storage__RootPath"] = appDataRoot;
        startInfo.Environment["Telegram__SessionsPath"] = Path.Combine(appDataRoot, "sessions");
        startInfo.Environment["AdminAuth__CredentialsPath"] = Path.Combine(appDataRoot, "admin_auth.json");
        startInfo.Environment["DataProtection__KeysPath"] = Path.Combine(appDataRoot, "keys");
        startInfo.Environment["Modules__RootPath"] = Path.Combine(appDataRoot, "modules");
        startInfo.Environment["LocalConfig__Path"] = Path.Combine(appDataRoot, "appsettings.local.json");
        startInfo.Environment["PanelSpa__RedirectLegacy"] = "true";
        startInfo.Environment["Serilog__Enabled"] = "true";
        startInfo.Environment["Serilog__FilePath"] = Path.Combine(DesktopPaths.LogsRoot, "telegram-panel-.log");

        _process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Web 服务进程启动失败。");

        _process.OutputDataReceived += (_, e) => AppendLog(e.Data);
        _process.ErrorDataReceived += (_, e) => AppendLog(e.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    private async Task<bool> IsHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(new Uri(BaseUri, "/healthz"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static void AppendLog(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        try
        {
            Directory.CreateDirectory(DesktopPaths.LogsRoot);
            File.AppendAllText(DesktopPaths.SetupLogPath, $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {line}{Environment.NewLine}");
        }
        catch
        {
            // 日志写入失败不能影响主程序运行。
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _process?.Dispose();
        _httpClient.Dispose();
    }
}
