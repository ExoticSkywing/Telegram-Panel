using System.Text.Json;

namespace TelegramPanel.Web.Services;

/// <summary>
/// 协调面板自更新包的启动状态，并在旧容器中安装新版入口脚本。
/// </summary>
internal static class SelfUpdateStartupCoordinator
{
    internal const string LegacyMarkerFileName = ".telegram-panel-self-update";
    internal const string PendingMarkerFileName = ".telegram-panel-update-pending";
    internal const string AttemptedMarkerFileName = ".telegram-panel-update-attempted";
    internal const string ConfirmedMarkerFileName = ".telegram-panel-update-confirmed";

    private const string PackagedEntrypointRelativePath = "self-update/entrypoint.sh";
    private const string ContainerEntrypointPath = "/entrypoint.sh";
    private const string EntrypointProtocolPrefix = "ENTRYPOINT_PROTOCOL_VERSION=";

    /// <summary>
    /// 必须在创建 WebApplicationBuilder 前调用。这样即使后续初始化失败，下一次容器启动也能识别失败并回滚。
    /// </summary>
    internal static void PrepareCurrentProcess(string applicationDirectory, Action<string>? log = null)
    {
        if (!IsRunningInContainer())
            return;

        var sourcePath = Path.Combine(applicationDirectory, PackagedEntrypointRelativePath);
        if (!File.Exists(sourcePath))
        {
            log?.Invoke($"更新包未包含入口脚本：{sourcePath}");
            return;
        }

        // 只有入口脚本安装成功后才把 pending 转成 attempted。
        // 否则旧入口脚本不认识 attempted，会失去自动回滚能力。
        if (TryInstallEntrypoint(sourcePath, ContainerEntrypointPath, log))
            TryMarkStartupAttempt(applicationDirectory, log);
    }

    internal static bool TryInstallEntrypoint(
        string sourcePath,
        string targetPath,
        Action<string>? log = null)
    {
        string? temporaryPath = null;
        try
        {
            if (!File.Exists(sourcePath))
                return false;

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (string.IsNullOrWhiteSpace(targetDirectory))
                throw new InvalidOperationException($"入口脚本目标路径无效：{targetPath}");

            var sourceProtocol = TryReadEntrypointProtocol(sourcePath);
            var targetProtocol = TryReadEntrypointProtocol(targetPath);
            if (sourceProtocol > 0 && targetProtocol >= sourceProtocol)
            {
                log?.Invoke($"容器入口协议 v{targetProtocol} 不低于更新包 v{sourceProtocol}，保留现有入口");
                return true;
            }

            Directory.CreateDirectory(targetDirectory);
            temporaryPath = Path.Combine(
                targetDirectory,
                $".{Path.GetFileName(targetPath)}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}");

            File.Copy(sourcePath, temporaryPath, overwrite: true);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    temporaryPath,
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite |
                    UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead |
                    UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead |
                    UnixFileMode.OtherExecute);
            }

            File.Move(temporaryPath, targetPath, overwrite: true);
            temporaryPath = null;
            log?.Invoke($"已安装自更新入口脚本：{targetPath}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"安装自更新入口脚本失败：{ex.Message}");
            return false;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(temporaryPath))
                TryDeleteFile(temporaryPath);
        }
    }

    internal static bool TryMarkStartupAttempt(string applicationDirectory, Action<string>? log = null)
    {
        var pendingPath = Path.Combine(applicationDirectory, PendingMarkerFileName);
        var attemptedPath = Path.Combine(applicationDirectory, AttemptedMarkerFileName);
        var confirmedPath = Path.Combine(applicationDirectory, ConfirmedMarkerFileName);
        var legacyPath = Path.Combine(applicationDirectory, LegacyMarkerFileName);
        try
        {
            if (File.Exists(pendingPath))
            {
                File.Move(pendingPath, attemptedPath, overwrite: true);
            }
            else if (File.Exists(legacyPath)
                     && !File.Exists(attemptedPath)
                     && !File.Exists(confirmedPath))
            {
                // v1.31.32 之前的更新器只写 legacy marker。新程序首次运行时补记 attempted，
                // 这样旧入口脚本启动本版后若初始化失败，下一次也能由刚安装的新入口自动回滚。
                File.Copy(legacyPath, attemptedPath, overwrite: false);
            }
            else
            {
                return false;
            }

            log?.Invoke("已记录新版本启动尝试，若本次启动未确认成功，下次容器启动将自动回滚");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"记录新版本启动尝试失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// 只能在 WebApplication.StartAsync 成功后调用。
    /// </summary>
    internal static bool TryConfirmSuccessfulStartup(
        string applicationDirectory,
        string version,
        Action<string>? log = null)
    {
        var pendingPath = Path.Combine(applicationDirectory, PendingMarkerFileName);
        var attemptedPath = Path.Combine(applicationDirectory, AttemptedMarkerFileName);
        if (!File.Exists(pendingPath) && !File.Exists(attemptedPath))
            return false;

        var confirmedPath = Path.Combine(applicationDirectory, ConfirmedMarkerFileName);
        var temporaryPath = $"{confirmedPath}.tmp-{Environment.ProcessId}-{Guid.NewGuid():N}";
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                version,
                confirmedAtUtc = DateTimeOffset.UtcNow
            });
            File.WriteAllText(temporaryPath, payload);
            File.Move(temporaryPath, confirmedPath, overwrite: true);

            // confirmed 必须先落盘；即使清理旧状态失败，入口脚本也会优先认可 confirmed。
            TryDeleteFile(pendingPath);
            TryDeleteFile(attemptedPath);
            log?.Invoke($"新版本 v{version} 已确认启动成功");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"确认新版本启动状态失败：{ex.Message}");
            return false;
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static bool IsRunningInContainer()
    {
        var value = Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER");
        return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || File.Exists("/.dockerenv");
    }

    private static int TryReadEntrypointProtocol(string path)
    {
        if (!File.Exists(path))
            return 0;

        foreach (var line in File.ReadLines(path).Take(32))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(EntrypointProtocolPrefix, StringComparison.Ordinal))
                continue;

            return int.TryParse(trimmed[EntrypointProtocolPrefix.Length..], out var version)
                ? version
                : 0;
        }

        return 0;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 状态文件清理失败不会覆盖原始启动结果。
        }
    }
}
