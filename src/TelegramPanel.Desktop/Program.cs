using System.Threading;

namespace TelegramPanel.Desktop;

internal static class Program
{
    private const string MutexName = "Local\\TelegramPanelDesktop";

    [STAThread]
    private static void Main(string[] args)
    {
        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            MessageBox.Show("Telegram Panel 已经在运行。", "Telegram Panel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowFatalError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                ShowFatalError(ex);
        };

        using var context = new DesktopApplicationContext(args);
        Application.Run(context);
    }

    private static void ShowFatalError(Exception exception)
    {
        MessageBox.Show(
            exception.Message,
            "Telegram Panel 运行异常",
            MessageBoxButtons.OK,
            MessageBoxIcon.Error);
    }
}
