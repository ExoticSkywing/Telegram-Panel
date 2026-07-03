using System.Diagnostics;
using System.IO.Compression;
using Microsoft.Win32;

namespace TelegramPanel.Setup;

internal sealed class InstallerService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public async Task InstallAsync(
        string installRoot,
        bool createDesktopShortcut,
        bool launchAtStartup,
        bool launchAfterInstall,
        IProgress<string> progress)
    {
        installRoot = NormalizeInstallRoot(installRoot);
        var launcherPath = SetupPaths.GetLauncherPath(installRoot);

        progress.Report("正在准备安装目录...");
        Directory.CreateDirectory(installRoot);
        Directory.CreateDirectory(SetupPaths.AppDataRoot);

        progress.Report("正在安装程序文件...");
        await Task.Run(() => InstallPayload(installRoot, progress));

        progress.Report("正在创建开始菜单快捷方式...");
        ShortcutHelper.CreateShortcut(
            SetupPaths.StartMenuShortcutPath,
            launcherPath,
            installRoot,
            SetupPaths.DisplayName);

        if (createDesktopShortcut)
        {
            progress.Report("正在创建桌面图标...");
            ShortcutHelper.CreateShortcut(
                SetupPaths.DesktopShortcutPath,
                launcherPath,
                installRoot,
                SetupPaths.DisplayName);
        }

        progress.Report("正在配置开机启动...");
        SetStartup(launchAtStartup, launcherPath);

        progress.Report("安装完成。");

        if (launchAfterInstall)
            Launch(launcherPath, installRoot);
    }

    private static string NormalizeInstallRoot(string installRoot)
    {
        installRoot = Environment.ExpandEnvironmentVariables((installRoot ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(installRoot))
            installRoot = SetupPaths.DefaultInstallRoot;

        var fullPath = Path.GetFullPath(installRoot);
        var root = Path.GetPathRoot(fullPath);
        if (string.Equals(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("安装位置不能是磁盘根目录，请选择一个独立文件夹。");

        return fullPath;
    }

    private static void InstallPayload(string targetDir, IProgress<string> progress)
    {
        using var stream = OpenPayloadStream();
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(Path.Combine(targetDir, entry.FullName));
                continue;
            }

            var fullPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            var normalizedRoot = Path.GetFullPath(targetDir);
            if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"安装包内包含非法路径：{entry.FullName}");

            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            entry.ExtractToFile(fullPath, overwrite: true);

            if (entry.FullName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                progress.Report($"已安装：{entry.FullName}");
        }
    }

    private static Stream OpenPayloadStream()
    {
        var assembly = typeof(InstallerService).Assembly;
        var embedded = assembly.GetManifestResourceStream("payload.zip");
        if (embedded != null)
            return embedded;

        if (Directory.Exists(SetupPaths.PayloadRoot))
        {
            var tempZip = Path.Combine(Path.GetTempPath(), $"telegram-panel-payload-{Guid.NewGuid():N}.zip");
            ZipFile.CreateFromDirectory(SetupPaths.PayloadRoot, tempZip, CompressionLevel.Fastest, includeBaseDirectory: false);
            return new FileStream(tempZip, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose);
        }

        throw new DirectoryNotFoundException("安装包缺少内置 payload，请重新构建安装包。");
    }

    private static void SetStartup(bool enabled, string launcherPath)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
            key.SetValue(SetupPaths.RunKeyName, $"\"{launcherPath}\"", RegistryValueKind.String);
        else
            key.DeleteValue(SetupPaths.RunKeyName, throwOnMissingValue: false);
    }

    private static void Launch(string launcherPath, string installRoot)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = launcherPath,
            WorkingDirectory = installRoot,
            UseShellExecute = true
        });
    }
}
