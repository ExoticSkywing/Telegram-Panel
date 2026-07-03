namespace TelegramPanel.Desktop;

internal static class DesktopPaths
{
    public const string AppName = "TelegramPanel";
    public const string RunKeyName = "TelegramPanelDesktop";
    public const string DefaultUrl = "http://127.0.0.1:18080";

    public static string ExecutablePath => Environment.ProcessPath ?? Application.ExecutablePath;

    public static string AppDataRoot =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);

    public static string WebRoot => Path.Combine(AppContext.BaseDirectory, "web");

    public static string WebExecutablePath => Path.Combine(WebRoot, "TelegramPanel.Web.exe");

    public static string WebViewUserDataRoot => Path.Combine(AppDataRoot, "webview2");

    public static string LogsRoot => Path.Combine(AppDataRoot, "logs");

    public static string SetupLogPath => Path.Combine(LogsRoot, "desktop.log");
}
