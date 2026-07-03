namespace TelegramPanel.Setup;

internal static class SetupPaths
{
    public const string AppName = "TelegramPanel";
    public const string DisplayName = "Telegram Panel";
    public const string RunKeyName = "TelegramPanelDesktop";

    public static string PayloadRoot => Path.Combine(AppContext.BaseDirectory, "payload");

    public static string DefaultInstallRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", AppName);

    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string DesktopShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"{DisplayName}.lnk");

    public static string StartMenuRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", DisplayName);

    public static string StartMenuShortcutPath => Path.Combine(StartMenuRoot, $"{DisplayName}.lnk");

    public static string GetLauncherPath(string installRoot) => Path.Combine(installRoot, "TelegramPanel.Desktop.exe");
}
