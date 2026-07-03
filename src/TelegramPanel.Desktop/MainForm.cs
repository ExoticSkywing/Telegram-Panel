using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace TelegramPanel.Desktop;

internal sealed class MainForm : Form
{
    private readonly Uri _startUri;
    private readonly WebView2 _webView = new()
    {
        Dock = DockStyle.Fill
    };

    private bool _allowClose;

    public MainForm(Uri baseUri)
    {
        _startUri = new Uri(baseUri, "/ui/dashboard");

        Text = "Telegram Panel";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1280, 820);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        Controls.Add(_webView);
        Load += OnLoadAsync;
        FormClosing += OnFormClosing;
    }

    public void AllowClose()
    {
        _allowClose = true;
    }

    public void ShowAndFocus()
    {
        if (WindowState == FormWindowState.Minimized)
            WindowState = FormWindowState.Normal;

        Show();
        Activate();
        BringToFront();
    }

    private async void OnLoadAsync(object? sender, EventArgs e)
    {
        try
        {
            Directory.CreateDirectory(DesktopPaths.WebViewUserDataRoot);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: DesktopPaths.WebViewUserDataRoot);
            await _webView.EnsureCoreWebView2Async(environment);
            _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = true;
            _webView.Source = _startUri;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"WebView2 初始化失败：{ex.Message}\n\n请安装 Microsoft Edge WebView2 Runtime 后重试。",
                "Telegram Panel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
            return;

        e.Cancel = true;
        Hide();
    }
}
