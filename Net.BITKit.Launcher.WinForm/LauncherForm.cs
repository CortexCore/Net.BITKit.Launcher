using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Net.BITKit.Launcher;
using Net.BITKit.Launcher.WinForm.Controls;

namespace Net.BITKit.Launcher.WinForm;

internal sealed class LauncherForm : Form
{
    private readonly string _workspace;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private readonly Label _titleLabel = new();
    private readonly Label _subtitleLabel = new();
    private readonly Label _announcementTitleLabel = new();
    private readonly Label _announcementContentLabel = new();
    private readonly LinkLabel _announcementLink = new();
    private readonly Label _statusLabel = new();
    private readonly Label _currentFileLabel = new();
    private readonly LinearProgressBar _progressBar = new();
    private readonly PrimaryButton _primaryButton = new();
    private readonly PrimaryButton _cancelButton = new();
    private readonly HeroCanvas _heroCanvas = new();

    private CancellationTokenSource? _operationCancellation;
    private UpdateService? _updateService;
    private UpdatePlan? _plan;
    private UpdateStage _currentStage = UpdateStage.Checking;
    private bool _busy;
    private bool _completed;
    private bool _closeWhenStopped;
    private bool _gameHero;
    private string? _announcementUrl;
    private string _launchButtonText = "启动游戏";

    public LauncherForm(string workspace, bool previewMode = false, bool autoInstall = false, bool simulationMode = false, string? simulationDisplayName = null)
    {
        _workspace = workspace;
        InitializeWindow();
        var initialManifest = TryLoadLocalManifest();
        _gameHero = !string.Equals(initialManifest?.Ui?.Preset, "compact", StringComparison.OrdinalIgnoreCase);
        if (_gameHero)
        {
            InitializeLayout();
        }
        else
        {
            ClientSize = new Size(760, 500);
            MinimumSize = new Size(680, 460);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            BackColor = Color.FromArgb(246, 247, 249);
            InitializeCompactLayout();
        }

        if (initialManifest is not null)
        {
            ApplyManifestPresentation(initialManifest);
        }

        if (simulationMode)
        {
            Shown += async (_, _) => await ShowSimulationUpdateAsync(simulationDisplayName);
        }
        else if (previewMode)
        {
            Shown += (_, _) => ShowPreviewState();
        }
        else if (autoInstall)
        {
            Shown += async (_, _) =>
            {
                await CheckForUpdatesAsync();
                if (_plan is not null && _updateService is not null)
                {
                    await HandlePrimaryActionAsync();
                }
            };
        }
        else
        {
            Shown += async (_, _) => await CheckForUpdatesAsync();
        }
        FormClosing += HandleFormClosing;
    }

    public int ExitCode { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            if (_gameHero)
            {
                parameters.ClassStyle |= 0x00020000;
            }

            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs eventArgs)
    {
        base.OnHandleCreated(eventArgs);
        if (!_gameHero || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            return;
        }

        const int DwmWindowCornerPreference = 33;
        var preference = 2;
        _ = DwmSetWindowAttribute(
            Handle,
            DwmWindowCornerPreference,
            ref preference,
            Marshal.SizeOf<int>());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _operationCancellation?.Dispose();
            _httpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeWindow()
    {
        Text = "Net.BITKit.Launcher";
        Icon = TryLoadApplicationIcon();
        ClientSize = new Size(1100, 620);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Color.FromArgb(5, 11, 26);
        Font = new Font("Segoe UI", 9.5F);
    }

    private static Icon? TryLoadApplicationIcon()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(Environment.ProcessPath)
                ? Icon.ExtractAssociatedIcon(Environment.ProcessPath)
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void InitializeLayout()
    {
        _heroCanvas.Dock = DockStyle.Fill;
        _heroCanvas.Artwork = LoadArtwork(null);
        Controls.Add(_heroCanvas);

        var topBar = new Panel
        {
            Dock = DockStyle.Top,
            Height = 150,
            BackColor = Color.Transparent
        };
        topBar.MouseDown += BeginWindowDrag;

        _titleLabel.AutoSize = false;
        _titleLabel.AutoEllipsis = true;
        _titleLabel.Font = new Font("Segoe UI", 17F, FontStyle.Bold);
        _titleLabel.ForeColor = Color.White;
        _titleLabel.BackColor = Color.Transparent;
        _titleLabel.Text = "BITKit Game";
        _titleLabel.Location = new Point(38, 6);
        _titleLabel.Size = new Size(520, 50);
        _titleLabel.TextAlign = ContentAlignment.MiddleLeft;
        _titleLabel.MouseDown += BeginWindowDrag;

        _subtitleLabel.AutoSize = false;
        _subtitleLabel.Font = new Font("Segoe UI", 9F);
        _subtitleLabel.ForeColor = Color.FromArgb(190, 211, 225, 245);
        _subtitleLabel.BackColor = Color.Transparent;
        _subtitleLabel.Text = "正在连接更新服务";
        _subtitleLabel.Location = new Point(40, 52);
        _subtitleLabel.Size = new Size(500, 30);
        _subtitleLabel.TextAlign = ContentAlignment.TopLeft;
        _subtitleLabel.MouseDown += BeginWindowDrag;

        var minimizeButton = new WindowCommandButton
        {
            Command = WindowCommand.Minimize,
            Location = new Point(0, 0)
        };
        minimizeButton.Click += (_, _) => WindowState = FormWindowState.Minimized;

        var closeButton = new WindowCommandButton
        {
            Command = WindowCommand.Close,
            Location = new Point(44, 0)
        };
        closeButton.Click += (_, _) => Close();

        var windowCommands = new Panel
        {
            Dock = DockStyle.Right,
            Width = 88,
            BackColor = Color.Transparent
        };
        windowCommands.Controls.Add(minimizeButton);
        windowCommands.Controls.Add(closeButton);

        var newsCaption = new Label
        {
            AutoSize = false,
            BackColor = Color.Transparent,
            ForeColor = Color.FromArgb(255, 210, 62),
            Font = new Font("Segoe UI", 8F, FontStyle.Bold),
            Location = new Point(40, 86),
            Size = new Size(100, 22),
            Text = "LATEST NEWS",
            TextAlign = ContentAlignment.MiddleLeft
        };

        _announcementTitleLabel.AutoSize = false;
        _announcementTitleLabel.AutoEllipsis = true;
        _announcementTitleLabel.BackColor = Color.Transparent;
        _announcementTitleLabel.ForeColor = Color.White;
        _announcementTitleLabel.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _announcementTitleLabel.Location = new Point(142, 86);
        _announcementTitleLabel.Size = new Size(350, 28);
        _announcementTitleLabel.Text = "公告";
        _announcementTitleLabel.TextAlign = ContentAlignment.MiddleLeft;

        _announcementContentLabel.AutoSize = false;
        _announcementContentLabel.AutoEllipsis = true;
        _announcementContentLabel.BackColor = Color.Transparent;
        _announcementContentLabel.ForeColor = Color.FromArgb(215, 226, 238);
        _announcementContentLabel.Location = new Point(40, 112);
        _announcementContentLabel.Size = new Size(430, 30);
        _announcementContentLabel.Text = "正在获取最新公告和版本信息…";
        _announcementContentLabel.TextAlign = ContentAlignment.MiddleLeft;

        _announcementLink.AutoSize = true;
        _announcementLink.BackColor = Color.Transparent;
        _announcementLink.LinkColor = Color.FromArgb(255, 210, 62);
        _announcementLink.ActiveLinkColor = Color.White;
        _announcementLink.Location = new Point(480, 115);
        _announcementLink.Text = "查看详情  ›";
        _announcementLink.Visible = false;
        _announcementLink.LinkClicked += (_, _) => OpenAnnouncement();

        topBar.Controls.Add(_titleLabel);
        topBar.Controls.Add(_subtitleLabel);
        topBar.Controls.Add(newsCaption);
        topBar.Controls.Add(_announcementTitleLabel);
        topBar.Controls.Add(_announcementContentLabel);
        topBar.Controls.Add(_announcementLink);
        topBar.Controls.Add(windowCommands);
        _heroCanvas.Controls.Add(topBar);

        _statusLabel.AutoSize = false;
        _statusLabel.AutoEllipsis = true;
        _statusLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.ForeColor = Color.White;
        _statusLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        _statusLabel.Location = new Point(48, ClientSize.Height - 132);
        _statusLabel.Size = new Size(700, 31);
        _statusLabel.Text = "正在检查更新…";

        _currentFileLabel.AutoSize = false;
        _currentFileLabel.AutoEllipsis = true;
        _currentFileLabel.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _currentFileLabel.BackColor = Color.Transparent;
        _currentFileLabel.ForeColor = Color.FromArgb(195, 211, 225, 240);
        _currentFileLabel.Location = new Point(49, ClientSize.Height - 100);
        _currentFileLabel.Size = new Size(700, 25);
        _currentFileLabel.Text = "请稍候";

        _progressBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _progressBar.BackColor = Color.Transparent;
        _progressBar.Location = new Point(49, ClientSize.Height - 61);
        _progressBar.Size = new Size(765, 8);
        _progressBar.TrackColor = Color.FromArgb(72, 255, 255, 255);
        _progressBar.BarColor = Color.FromArgb(255, 205, 55);

        _primaryButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _primaryButton.Location = new Point(ClientSize.Width - 238, ClientSize.Height - 112);
        _primaryButton.Size = new Size(190, 58);
        _primaryButton.Font = new Font("Segoe UI", 12F, FontStyle.Bold);
        _primaryButton.ForeColor = Color.FromArgb(15, 23, 42);
        _primaryButton.PrimaryColor = Color.FromArgb(255, 205, 55);
        _primaryButton.HoverColor = Color.FromArgb(255, 218, 91);
        _primaryButton.PressedColor = Color.FromArgb(232, 180, 31);
        _primaryButton.DisabledColor = Color.FromArgb(120, 148, 163, 184);
        _primaryButton.CornerRadius = 13;
        _primaryButton.Text = "检查更新";
        _primaryButton.Enabled = false;
        _primaryButton.Click += async (_, _) => await HandlePrimaryActionAsync();

        _cancelButton.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
        _cancelButton.Location = new Point(ClientSize.Width - 336, ClientSize.Height - 101);
        _cancelButton.Size = new Size(88, 42);
        _cancelButton.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
        _cancelButton.ForeColor = Color.White;
        _cancelButton.PrimaryColor = Color.FromArgb(135, 11, 20, 40);
        _cancelButton.HoverColor = Color.FromArgb(185, 28, 39, 61);
        _cancelButton.PressedColor = Color.FromArgb(210, 4, 10, 25);
        _cancelButton.DisabledColor = Color.FromArgb(90, 15, 23, 42);
        _cancelButton.BorderColor = Color.FromArgb(90, 255, 255, 255);
        _cancelButton.BorderWidth = 1;
        _cancelButton.CornerRadius = 10;
        _cancelButton.Text = "取消";
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => CancelCurrentOperation();

        _heroCanvas.Controls.Add(_statusLabel);
        _heroCanvas.Controls.Add(_currentFileLabel);
        _heroCanvas.Controls.Add(_progressBar);
        _heroCanvas.Controls.Add(_primaryButton);
        _heroCanvas.Controls.Add(_cancelButton);
    }

    private void InitializeCompactLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var header = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.White
        };
        header.Paint += (_, eventArgs) =>
        {
            using var border = new Pen(Color.FromArgb(234, 236, 240));
            eventArgs.Graphics.DrawLine(border, 0, header.Height - 1, header.Width, header.Height - 1);
        };

        var headerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(30, 17, 30, 17),
            ColumnCount = 2,
            RowCount = 1
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 58));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        headerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var appMark = new AppMark
        {
            Anchor = AnchorStyles.Left
        };
        headerLayout.Controls.Add(appMark, 0, 0);

        var heading = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty
        };
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 42));

        _titleLabel.AutoSize = false;
        _titleLabel.AutoEllipsis = true;
        _titleLabel.Dock = DockStyle.Fill;
        _titleLabel.Font = new Font("Segoe UI", 15F, FontStyle.Bold);
        _titleLabel.ForeColor = Color.FromArgb(17, 24, 39);
        _titleLabel.BackColor = Color.Transparent;
        _titleLabel.Text = "BITKit Launcher";
        _titleLabel.TextAlign = ContentAlignment.BottomLeft;

        _subtitleLabel.AutoSize = false;
        _subtitleLabel.Dock = DockStyle.Fill;
        _subtitleLabel.ForeColor = Color.FromArgb(107, 114, 128);
        _subtitleLabel.BackColor = Color.Transparent;
        _subtitleLabel.Text = "正在准备检查更新";
        _subtitleLabel.TextAlign = ContentAlignment.TopLeft;
        heading.Controls.Add(_titleLabel, 0, 0);
        heading.Controls.Add(_subtitleLabel, 0, 1);
        headerLayout.Controls.Add(heading, 1, 0);
        header.Controls.Add(headerLayout);
        root.Controls.Add(header, 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = BackColor,
            Padding = new Padding(30, 24, 30, 22),
            ColumnCount = 1,
            RowCount = 3
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        content.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.Controls.Add(content, 0, 1);

        var announcementCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FillColor = Color.FromArgb(239, 246, 255),
            BorderColor = Color.FromArgb(219, 234, 254),
            CornerRadius = 14,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        var announcementLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2
        };
        announcementLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        announcementLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        announcementLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        announcementLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _announcementTitleLabel.AutoSize = false;
        _announcementTitleLabel.Dock = DockStyle.Fill;
        _announcementTitleLabel.Font = new Font("Segoe UI", 10.5F, FontStyle.Bold);
        _announcementTitleLabel.ForeColor = Color.FromArgb(30, 64, 175);
        _announcementTitleLabel.BackColor = Color.Transparent;
        _announcementTitleLabel.Text = "公告";
        _announcementTitleLabel.TextAlign = ContentAlignment.MiddleLeft;

        _announcementContentLabel.AutoSize = false;
        _announcementContentLabel.AutoEllipsis = true;
        _announcementContentLabel.Dock = DockStyle.Fill;
        _announcementContentLabel.ForeColor = Color.FromArgb(55, 65, 81);
        _announcementContentLabel.BackColor = Color.Transparent;
        _announcementContentLabel.Text = "正在获取最新公告和版本信息…";
        _announcementContentLabel.TextAlign = ContentAlignment.TopLeft;

        _announcementLink.AutoSize = true;
        _announcementLink.Text = "查看详情";
        _announcementLink.Visible = false;
        _announcementLink.LinkColor = Color.FromArgb(37, 99, 235);
        _announcementLink.ActiveLinkColor = Color.FromArgb(30, 64, 175);
        _announcementLink.BackColor = Color.Transparent;
        _announcementLink.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        _announcementLink.Margin = new Padding(18, 3, 0, 0);
        _announcementLink.LinkClicked += (_, _) => OpenAnnouncement();

        announcementLayout.Controls.Add(_announcementTitleLabel, 0, 0);
        announcementLayout.SetColumnSpan(_announcementTitleLabel, 2);
        announcementLayout.Controls.Add(_announcementContentLabel, 0, 1);
        announcementLayout.Controls.Add(_announcementLink, 1, 1);
        announcementCard.Controls.Add(announcementLayout);
        content.Controls.Add(announcementCard, 0, 0);

        var statusCard = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            FillColor = Color.White,
            BorderColor = Color.FromArgb(229, 231, 235),
            CornerRadius = 14,
            Padding = new Padding(22, 20, 22, 18),
            Margin = new Padding(0, 16, 0, 16)
        };
        var statusLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        statusLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));

        _statusLabel.AutoSize = false;
        _statusLabel.Font = new Font("Segoe UI", 13F, FontStyle.Bold);
        _statusLabel.ForeColor = Color.FromArgb(17, 24, 39);
        _statusLabel.BackColor = Color.Transparent;
        _statusLabel.Text = "正在检查更新…";
        _statusLabel.Dock = DockStyle.Fill;
        _statusLabel.TextAlign = ContentAlignment.MiddleLeft;

        _currentFileLabel.AutoSize = false;
        _currentFileLabel.AutoEllipsis = true;
        _currentFileLabel.BackColor = Color.Transparent;
        _currentFileLabel.ForeColor = Color.FromArgb(107, 114, 128);
        _currentFileLabel.Text = "请稍候";
        _currentFileLabel.Dock = DockStyle.Fill;
        _currentFileLabel.TextAlign = ContentAlignment.TopLeft;

        _progressBar.Dock = DockStyle.Fill;
        statusLayout.Controls.Add(_statusLabel, 0, 0);
        statusLayout.Controls.Add(_currentFileLabel, 0, 1);
        statusLayout.Controls.Add(_progressBar, 0, 2);
        statusCard.Controls.Add(statusLayout);
        content.Controls.Add(statusCard, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 8, 0, 0)
        };
        _primaryButton.Text = "检查更新";
        _primaryButton.Width = 156;
        _primaryButton.CornerRadius = 10;
        _primaryButton.Enabled = false;
        _primaryButton.Click += async (_, _) => await HandlePrimaryActionAsync();

        _cancelButton.Text = "取消";
        _cancelButton.Width = 96;
        _cancelButton.PrimaryColor = Color.White;
        _cancelButton.HoverColor = Color.FromArgb(249, 250, 251);
        _cancelButton.PressedColor = Color.FromArgb(243, 244, 246);
        _cancelButton.DisabledColor = Color.FromArgb(249, 250, 251);
        _cancelButton.BorderColor = Color.FromArgb(209, 213, 219);
        _cancelButton.BorderWidth = 1;
        _cancelButton.ForeColor = Color.FromArgb(55, 65, 81);
        _cancelButton.CornerRadius = 10;
        _cancelButton.Visible = false;
        _cancelButton.Click += (_, _) => CancelCurrentOperation();

        buttons.Controls.Add(_primaryButton);
        buttons.Controls.Add(_cancelButton);
        content.Controls.Add(buttons, 0, 2);
    }

    private void ShowPreviewState()
    {
        _statusLabel.Text = "界面预览";
        _statusLabel.ForeColor = DefaultStatusColor;
        _currentFileLabel.Text = "离线模式：联网检查、下载和启动操作均已禁用";
        _progressBar.Value = 0;
        _primaryButton.Text = "预览模式";
        _primaryButton.Enabled = false;
        _cancelButton.Visible = false;
    }

    private async Task ShowSimulationUpdateAsync(string? displayName)
    {
        _titleLabel.Text = string.IsNullOrWhiteSpace(displayName) ? "BITKit Game" : displayName;
        _subtitleLabel.Text = "模拟更新 · 不会下载游戏文件";
        _announcementTitleLabel.Text = "SIMULATION";
        _announcementContentLabel.Text = "仅演示更新界面，未下载或修改任何游戏文件";
        _statusLabel.Text = "模拟更新准备中";
        _statusLabel.ForeColor = Color.FromArgb(255, 210, 62);
        _currentFileLabel.Text = "启动器已复制，正在演示更新流程";
        _primaryButton.Enabled = false;
        _cancelButton.Visible = false;
        _progressBar.Value = 0;

        var sampleFiles = new[] { "LikeTokyo.exe", "UnityPlayer.dll", "GameAssembly.dll", "StreamingAssets", "resources.assets" };
        for (var percent = 0; percent <= 100; percent += 2)
        {
            await Task.Delay(35);
            if (IsDisposed) return;
            _progressBar.Value = percent;
            _statusLabel.Text = $"模拟更新 {percent}% · 不会下载游戏文件";
            _currentFileLabel.Text = sampleFiles[Math.Min(sampleFiles.Length - 1, percent * sampleFiles.Length / 101)];
        }

        _statusLabel.Text = "模拟更新完成";
        _statusLabel.ForeColor = SuccessStatusColor;
        _currentFileLabel.Text = "演示结束，未下载或修改游戏文件";
        _primaryButton.Text = "关闭";
        _primaryButton.Enabled = true;
        _completed = true;
    }

    private async Task CheckForUpdatesAsync()
    {
        BeginOperation("正在检查更新…");
        _plan = null;
        _completed = false;
        try
        {
            var progress = new Progress<UpdateProgress>(HandleProgress);
            _updateService = new UpdateService(new ManifestService(), _httpClient, progress: progress);
            _plan = await _updateService.BuildPlanAsync(_workspace, _operationCancellation!.Token);
            ApplyManifestPresentation(_plan.RemoteManifest);

            var changes = _plan.Downloads.Count + _plan.Deletions.Count;
            _statusLabel.Text = changes == 0 ? "当前已经是最新版本" : $"发现 {changes} 项更新";
            _statusLabel.ForeColor = DefaultStatusColor;
            _currentFileLabel.Text = changes == 0
                ? "点击启动应用"
                : $"需要下载 {_plan.Downloads.Count} 个文件，删除 {_plan.Deletions.Count} 个旧文件";
            _primaryButton.Text = changes == 0 ? _launchButtonText : "获取更新";
            _primaryButton.Enabled = true;
            ExitCode = LauncherExitCodes.Success;
        }
        catch (OperationCanceledException)
        {
            HandleCancellation();
        }
        catch (Exception exception)
        {
            ShowError("检查更新失败", exception);
        }
        finally
        {
            EndOperation();
        }
    }

    private async Task HandlePrimaryActionAsync()
    {
        if (_completed)
        {
            Close();
            return;
        }

        if (_plan is null || _updateService is null)
        {
            await CheckForUpdatesAsync();
            return;
        }

        var blockers = RestartManager.GetBlockingProcesses(RestartManager.GetChangedPaths(_plan));
        if (blockers.Count > 0)
        {
            ShowBlockingProcesses(blockers);
            return;
        }

        BeginOperation("正在下载更新…");
        var closeAfterOperation = false;
        try
        {
            var result = await _updateService.ApplyAsync(
                _workspace,
                _plan,
                maxConcurrency: 4,
                launchAfterUpdate: true,
                _operationCancellation!.Token);

            if (result.SelfUpdateScheduled)
            {
                _statusLabel.Text = "正在完成启动器自更新…";
                _currentFileLabel.Text = "窗口关闭后将替换启动器并启动应用";
                ExitCode = LauncherExitCodes.Success;
                closeAfterOperation = true;
            }
            else
            {
                _completed = true;
                _progressBar.Value = 100;
                _statusLabel.Text = "更新完成";
                _statusLabel.ForeColor = SuccessStatusColor;
                _currentFileLabel.Text = "正在启动应用…";
                _primaryButton.Text = "关闭";
                _primaryButton.Enabled = true;
                ExitCode = LauncherExitCodes.Success;

                if (!string.IsNullOrWhiteSpace(_plan.RemoteManifest.Launcher))
                {
                    ProcessLauncher.Launch(
                        _workspace,
                        _plan.RemoteManifest.Launcher,
                        _plan.RemoteManifest.GameArguments,
                        _plan.RemoteManifest.GameEnvironment);
                    closeAfterOperation = true;
                }
            }
        }
        catch (OperationCanceledException)
        {
            HandleCancellation();
        }
        catch (Exception exception)
        {
            ShowError("更新失败", exception);
        }
        finally
        {
            EndOperation();
        }

        if (closeAfterOperation && !IsDisposed)
        {
            Close();
        }
    }

    private void HandleProgress(UpdateProgress progress)
    {
        if (IsDisposed || !_busy)
        {
            return;
        }

        _currentStage = progress.Stage;
        _statusLabel.Text = progress.Message;
        if (!string.IsNullOrWhiteSpace(progress.CurrentFile))
        {
            _currentFileLabel.Text = progress.CurrentFile;
        }

        _progressBar.Value = progress.Stage switch
        {
            UpdateStage.Applying or UpdateStage.Completed => 100,
            _ when progress.BytesTotal > 0 => (int)Math.Clamp(
                progress.BytesCompleted * 100L / progress.BytesTotal,
                0,
                100),
            _ => 0
        };
    }

    private void ApplyManifestPresentation(Manifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Name))
        {
            _titleLabel.Text = manifest.Name;
        }

        _subtitleLabel.Text = string.IsNullOrWhiteSpace(manifest.Version)
            ? "已连接更新服务"
            : $"最新版本 {manifest.Version}";

        var announcement = manifest.Announcement;
        _announcementTitleLabel.Text = string.IsNullOrWhiteSpace(announcement?.Title)
            ? "公告"
            : announcement.Title;
        _announcementContentLabel.Text = string.IsNullOrWhiteSpace(announcement?.Content)
            ? "暂无公告，启动器会保持应用为最新版本。"
            : announcement.Content;
        _announcementUrl = announcement?.Url;
        _announcementLink.Visible = IsHttpUrl(_announcementUrl);
        ApplyTheme(manifest.Ui);
    }

    private void ShowBlockingProcesses(IReadOnlyList<BlockingProcess> blockers)
    {
        _statusLabel.Text = "请先关闭正在使用更新文件的程序";
        _statusLabel.ForeColor = WarningStatusColor;
        _currentFileLabel.Text = string.Join(
            "  ·  ",
            blockers.Take(4).Select(process => $"{process.Name} (PID {process.ProcessId})"));
        _primaryButton.Text = "重新检测并更新";
        _primaryButton.Enabled = true;
        ExitCode = LauncherExitCodes.FilesInUse;
    }

    private void ShowError(string title, Exception exception)
    {
        _plan = null;
        _statusLabel.Text = title;
        _statusLabel.ForeColor = ErrorStatusColor;
        _currentFileLabel.Text = exception.Message;
        _progressBar.Value = 0;
        _primaryButton.Text = "重新检查";
        _primaryButton.Enabled = true;
        ExitCode = LauncherExitCodes.Failure;
    }

    private void BeginOperation(string message)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _busy = true;
        _statusLabel.Text = message;
        _statusLabel.ForeColor = DefaultStatusColor;
        _primaryButton.Enabled = false;
        _cancelButton.Visible = true;
    }

    private void EndOperation()
    {
        _busy = false;
        _cancelButton.Visible = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;

        if (_closeWhenStopped && !IsDisposed)
        {
            BeginInvoke(Close);
        }
    }

    private void CancelCurrentOperation()
    {
        if (_currentStage == UpdateStage.Applying)
        {
            _statusLabel.Text = "正在应用更新，此阶段不能中断…";
            return;
        }

        _statusLabel.Text = "正在取消…";
        _operationCancellation?.Cancel();
    }

    private void HandleCancellation()
    {
        _statusLabel.Text = "操作已取消";
        _currentFileLabel.Text = "可以重新检查更新";
        _primaryButton.Text = "重新检查";
        _primaryButton.Enabled = true;
        ExitCode = LauncherExitCodes.Cancelled;
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!_busy)
        {
            return;
        }

        eventArgs.Cancel = true;
        if (_currentStage == UpdateStage.Applying)
        {
            _statusLabel.Text = "正在应用更新，请稍候…";
            return;
        }

        _closeWhenStopped = true;
        CancelCurrentOperation();
    }

    private Manifest? TryLoadLocalManifest()
    {
        try
        {
            var path = Path.Combine(_workspace, ManifestService.FileName);
            return File.Exists(path)
                ? new ManifestService().Deserialize(File.ReadAllText(path))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private void ApplyTheme(LauncherUi? ui)
    {
        if (!_gameHero)
        {
            return;
        }

        _launchButtonText = string.IsNullOrWhiteSpace(ui?.ButtonText)
            ? "启动游戏"
            : ui.ButtonText;

        var accent = ParseColor(ui?.AccentColor, Color.FromArgb(255, 205, 55));
        _primaryButton.PrimaryColor = accent;
        _primaryButton.HoverColor = Blend(accent, Color.White, 0.2F);
        _primaryButton.PressedColor = Blend(accent, Color.Black, 0.14F);
        _progressBar.BarColor = accent;
        _announcementLink.LinkColor = accent;

        if (!string.IsNullOrWhiteSpace(ui?.Background))
        {
            var artwork = LoadArtwork(ui.Background);
            if (artwork is not null)
            {
                _heroCanvas.Artwork = artwork;
            }
        }
    }

    private Image? LoadArtwork(string? relativePath)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(relativePath))
            {
                var path = PathSafety.ResolveUnderRoot(_workspace, relativePath);
                if (File.Exists(path))
                {
                    var bytes = File.ReadAllBytes(path);
                    using var memory = new MemoryStream(bytes);
                    using var source = Image.FromStream(memory);
                    return new Bitmap(source);
                }

                return null;
            }

            const string resourceName = "Net.BITKit.Launcher.WinForm.Assets.default-hero.png";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName)
                               ?? throw new InvalidOperationException("内置启动器背景不存在。");
            using var embedded = Image.FromStream(stream);
            return new Bitmap(embedded);
        }
        catch
        {
            return null;
        }
    }

    private void BeginWindowDrag(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button != MouseButtons.Left)
        {
            return;
        }

        _ = ReleaseCapture();
        _ = SendMessage(Handle, 0x00A1, 0x0002, IntPtr.Zero);
    }

    private Color DefaultStatusColor => _gameHero
        ? Color.White
        : Color.FromArgb(31, 41, 55);

    private Color SuccessStatusColor => _gameHero
        ? Color.FromArgb(134, 239, 172)
        : Color.FromArgb(22, 101, 52);

    private Color WarningStatusColor => _gameHero
        ? Color.FromArgb(253, 224, 71)
        : Color.FromArgb(180, 83, 9);

    private Color ErrorStatusColor => _gameHero
        ? Color.FromArgb(253, 164, 175)
        : Color.FromArgb(185, 28, 28);

    private static Color ParseColor(string? value, Color fallback)
    {
        try
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : ColorTranslator.FromHtml(value);
        }
        catch
        {
            return fallback;
        }
    }

    private static Color Blend(Color foreground, Color background, float amount)
    {
        amount = Math.Clamp(amount, 0F, 1F);
        return Color.FromArgb(
            (int)(foreground.R + ((background.R - foreground.R) * amount)),
            (int)(foreground.G + ((background.G - foreground.G) * amount)),
            (int)(foreground.B + ((background.B - foreground.B) * amount)));
    }

    private void OpenAnnouncement()
    {
        if (!IsHttpUrl(_announcementUrl))
        {
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = _announcementUrl!,
            UseShellExecute = true
        });
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme is "http" or "https";

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr window, int message, int wParam, IntPtr lParam);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr window,
        int attribute,
        ref int value,
        int valueSize);
}
