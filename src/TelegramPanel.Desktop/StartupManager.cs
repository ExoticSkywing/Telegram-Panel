using Microsoft.Win32;

namespace TelegramPanel.Desktop;

internal static class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var value = key?.GetValue(DesktopPaths.RunKeyName) as string;
        return string.Equals(value, Quote(DesktopPaths.ExecutablePath), StringComparison.OrdinalIgnoreCase);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
            ?? Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
            key.SetValue(DesktopPaths.RunKeyName, Quote(DesktopPaths.ExecutablePath), RegistryValueKind.String);
        else
            key.DeleteValue(DesktopPaths.RunKeyName, throwOnMissingValue: false);
    }

    private static string Quote(string value) => $"\"{value}\"";
}
