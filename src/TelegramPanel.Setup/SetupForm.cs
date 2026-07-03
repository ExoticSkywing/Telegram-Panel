namespace TelegramPanel.Setup;

internal sealed class SetupForm : Form
{
    private readonly InstallerService _installer = new();
    private readonly Label _statusLabel = new();
    private readonly ProgressBar _progressBar = new();
    private readonly CheckBox _desktopShortcutCheckBox = new();
    private readonly CheckBox _startupCheckBox = new();
    private readonly CheckBox _launchCheckBox = new();
    private readonly TextBox _installPathTextBox = new();
    private readonly Button _browseButton = new();
    private readonly Button _installButton = new();
    private readonly Button _cancelButton = new();

    public SetupForm()
    {
        Text = "Telegram Panel 安装程序";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 400);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        var title = new Label
        {
            Text = "Telegram Panel",
            Font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold),
            AutoSize = true,
            Location = new Point(30, 28)
        };

        var subtitle = new Label
        {
            Text = "安装 Windows 桌面版，后台服务将随托盘程序运行。",
            AutoSize = true,
            Location = new Point(32, 72)
        };

        var installPathLabel = new Label
        {
            Text = "安装位置：",
            AutoSize = true,
            Location = new Point(32, 112)
        };

        _installPathTextBox.Text = SetupPaths.DefaultInstallRoot;
        _installPathTextBox.Size = new Size(460, 28);
        _installPathTextBox.Location = new Point(102, 108);

        _browseButton.Text = "浏览...";
        _browseButton.Size = new Size(72, 28);
        _browseButton.Location = new Point(568, 107);
        _browseButton.Click += OnBrowseClick;

        _desktopShortcutCheckBox.Text = "创建桌面图标";
        _desktopShortcutCheckBox.Checked = true;
        _desktopShortcutCheckBox.AutoSize = true;
        _desktopShortcutCheckBox.Location = new Point(34, 166);

        _startupCheckBox.Text = "开机启动并驻留托盘";
        _startupCheckBox.Checked = true;
        _startupCheckBox.AutoSize = true;
        _startupCheckBox.Location = new Point(34, 198);

        _launchCheckBox.Text = "安装完成后立即运行";
        _launchCheckBox.Checked = true;
        _launchCheckBox.AutoSize = true;
        _launchCheckBox.Location = new Point(34, 230);

        _statusLabel.Text = "准备安装";
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Size = new Size(580, 24);
        _statusLabel.Location = new Point(32, 304);

        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Value = 0;
        _progressBar.Size = new Size(580, 12);
        _progressBar.Location = new Point(32, 332);

        _installButton.Text = "安装";
        _installButton.Size = new Size(96, 34);
        _installButton.Location = new Point(404, 358);
        _installButton.Click += OnInstallClickAsync;

        _cancelButton.Text = "取消";
        _cancelButton.Size = new Size(96, 34);
        _cancelButton.Location = new Point(512, 358);
        _cancelButton.Click += (_, _) => Close();

        Controls.AddRange(new Control[]
        {
            title,
            subtitle,
            installPathLabel,
            _installPathTextBox,
            _browseButton,
            _desktopShortcutCheckBox,
            _startupCheckBox,
            _launchCheckBox,
            _statusLabel,
            _progressBar,
            _installButton,
            _cancelButton
        });
    }

    private void OnBrowseClick(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 Telegram Panel 安装位置",
            UseDescriptionForTitle = true,
            SelectedPath = string.IsNullOrWhiteSpace(_installPathTextBox.Text)
                ? SetupPaths.DefaultInstallRoot
                : _installPathTextBox.Text
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installPathTextBox.Text = dialog.SelectedPath;
    }

    private async void OnInstallClickAsync(object? sender, EventArgs e)
    {
        SetInstalling(true);

        var progress = new Progress<string>(message =>
        {
            _statusLabel.Text = message;
            if (_progressBar.Value < 95)
                _progressBar.Value += 5;
        });

        try
        {
            await _installer.InstallAsync(
                _installPathTextBox.Text,
                _desktopShortcutCheckBox.Checked,
                _startupCheckBox.Checked,
                _launchCheckBox.Checked,
                progress);

            _progressBar.Value = 100;
            MessageBox.Show("Telegram Panel 已安装完成。", "Telegram Panel", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = "安装失败";
            MessageBox.Show(ex.Message, "Telegram Panel 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetInstalling(false);
        }
    }

    private void SetInstalling(bool installing)
    {
        _installButton.Enabled = !installing;
        _cancelButton.Enabled = !installing;
        _desktopShortcutCheckBox.Enabled = !installing;
        _startupCheckBox.Enabled = !installing;
        _launchCheckBox.Enabled = !installing;
        _installPathTextBox.Enabled = !installing;
        _browseButton.Enabled = !installing;
        _progressBar.Style = installing ? ProgressBarStyle.Marquee : ProgressBarStyle.Continuous;
    }
}
