using System.Diagnostics;

namespace TelegramPanel.Desktop;

internal sealed class DesktopApplicationContext : ApplicationContext
{
    private readonly WebHostManager _webHost = new();
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _startupMenuItem;
    private MainForm? _mainForm;
    private bool _exiting;

    public DesktopApplicationContext(string[] args)
    {
        var icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

        _startupMenuItem = new ToolStripMenuItem("开机启动")
        {
            Checked = StartupManager.IsEnabled(),
            CheckOnClick = true
        };
        _startupMenuItem.CheckedChanged += OnStartupMenuItemCheckedChanged;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开面板", null, (_, _) => ShowMainWindow());
        menu.Items.Add("在浏览器打开", null, (_, _) => OpenInBrowser());
        menu.Items.Add(_startupMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Text = "Telegram Panel",
            Icon = icon,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => ShowMainWindow();

        _ = StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await _webHost.StartAsync(CancellationToken.None);
            _mainForm = new MainForm(_webHost.BaseUri);
            _mainForm.FormClosed += (_, _) =>
            {
                if (_exiting)
                    ExitThread();
            };
            _mainForm.ShowAndFocus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Telegram Panel 启动失败：{ex.Message}",
                "Telegram Panel",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            ExitApplication();
        }
    }

    private void ShowMainWindow()
    {
        if (_mainForm == null || _mainForm.IsDisposed)
            _mainForm = new MainForm(_webHost.BaseUri);

        _mainForm.ShowAndFocus();
    }

    private void OpenInBrowser()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = new Uri(_webHost.BaseUri, "/ui/dashboard").ToString(),
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Telegram Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnStartupMenuItemCheckedChanged(object? sender, EventArgs e)
    {
        SetStartup(_startupMenuItem.Checked);
    }

    private void SetStartup(bool enabled)
    {
        try
        {
            StartupManager.SetEnabled(enabled);
        }
        catch (Exception ex)
        {
            _startupMenuItem.CheckedChanged -= OnStartupMenuItemCheckedChanged;
            _startupMenuItem.Checked = StartupManager.IsEnabled();
            _startupMenuItem.CheckedChanged += OnStartupMenuItemCheckedChanged;
            MessageBox.Show($"开机启动设置失败：{ex.Message}", "Telegram Panel", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExitApplication()
    {
        if (_exiting)
            return;

        _exiting = true;
        _notifyIcon.Visible = false;
        _mainForm?.AllowClose();
        _mainForm?.Close();
        _webHost.Stop();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _notifyIcon.Dispose();
            _webHost.Dispose();
            _mainForm?.Dispose();
        }

        base.Dispose(disposing);
    }
}
