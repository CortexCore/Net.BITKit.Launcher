using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Net.BITKit.Launcher;

namespace Net.BITKit.Launcher.WinForm;

public sealed partial class MainWindow : Window
{
    private const string InstalledMarkerName = ".bitkit-install.json";

    private readonly WindowMode _mode;
    private readonly BootstrapConfiguration? _bootstrap;
    private readonly LauncherDataStore? _dataStore;
    private readonly bool _installDryRun;
    private readonly string _workspace = "";
    private readonly bool _previewMode;
    private readonly bool _autoInstall;
    private readonly bool _simulationMode;
    private readonly string? _simulationDisplayName;
    private readonly HttpClient _httpClient;

    private CancellationTokenSource? _operationCancellation;
    private UpdateService? _updateService;
    private UpdatePlan? _plan;
    private Manifest? _remoteManifest;
    private Stopwatch? _downloadStopwatch;
    private long _requiredBytes;
    private UpdateStage _currentStage = UpdateStage.Checking;
    private string _primaryMode = "check";
    private string? _announcementUrl;
    private string _launchButtonText = "开始行动";
    private bool _busy;
    private bool _completed;
    private bool _closeWhenStopped;
    private bool _initialized;

    internal MainWindow(BootstrapConfiguration bootstrap, LauncherDataStore dataStore, bool installDryRun = false)
    {
        _mode = WindowMode.Installer;
        _bootstrap = bootstrap;
        _dataStore = dataStore;
        _installDryRun = installDryRun;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };

        InitializeComponent();
        LoadEmbeddedArtwork();
        ProductNameText.Text = bootstrap.DisplayName.ToUpperInvariant();
        Title = (installDryRun ? "[模拟] " : "") + $"安装 {bootstrap.DisplayName}";
        ServiceStatusText.Text = installDryRun ? "模拟安装服务" : "安装服务在线";
        InstallPathTextBox.Text = RecommendInstallPath();
        DeployButtonText.Text = installDryRun ? "模拟部署" : "开始部署";
        VerifyButton.IsEnabled = false;
        SetPhase("install");
    }

    internal MainWindow(
        string workspace,
        bool previewMode = false,
        bool autoInstall = false,
        bool simulationMode = false,
        string? simulationDisplayName = null)
    {
        _mode = WindowMode.Launcher;
        _workspace = Path.GetFullPath(workspace);
        _previewMode = previewMode;
        _autoInstall = autoInstall;
        _simulationMode = simulationMode;
        _simulationDisplayName = simulationDisplayName;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

        InitializeComponent();
        LoadEmbeddedArtwork();
        ProductNameText.Text = (simulationDisplayName ?? "BITKit Game").ToUpperInvariant();
        DesktopShortcutCheckBox.IsEnabled = false;
        SetPhase("sync");

        var localManifest = TryLoadLocalManifest();
        if (localManifest is not null)
        {
            ApplyManifestPresentation(localManifest);
        }
    }

    public string? InstalledPath { get; private set; }
    public string? InstalledLauncherPath { get; private set; }
    public bool SimulationCompleted { get; private set; }
    public int ExitCode { get; private set; }

    protected override void OnClosed(EventArgs e)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _httpClient.Dispose();
        base.OnClosed(e);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        if (_mode == WindowMode.Installer)
        {
            await PrepareInstallerAsync();
            return;
        }

        if (_simulationMode)
        {
            await ShowSimulationUpdateAsync();
            return;
        }

        if (_previewMode)
        {
            ShowPreview();
            return;
        }

        await CheckForUpdatesAsync();
        if (_autoInstall && _plan is not null && _updateService is not null)
        {
            await ApplyAndLaunchAsync();
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_busy)
        {
            return;
        }

        e.Cancel = true;
        if (_mode == WindowMode.Installer || _currentStage == UpdateStage.Applying)
        {
            if (_mode == WindowMode.Installer)
            {
                SetInstallerStatus("正在准备安装，请稍候", "创建安装记录时不能关闭启动器", "warning");
            }
            else
            {
                SetSyncStatus("正在应用更新，请稍候", "文件替换阶段不能中断", "warning");
            }

            return;
        }

        _closeWhenStopped = true;
        CancelCurrentOperation();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _mode != WindowMode.Installer)
        {
            return;
        }

        var currentPath = InstallPathTextBox.Text;
        var dialog = new OpenFolderDialog
        {
            Title = $"选择 {_bootstrap!.DisplayName} 的安装目录",
            InitialDirectory = Directory.Exists(currentPath)
                ? currentPath
                : Path.GetDirectoryName(currentPath)
        };
        if (dialog.ShowDialog(this) == true)
        {
            InstallPathTextBox.Text = dialog.FolderName;
        }
    }

    private async void DeployButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy && _mode == WindowMode.Installer)
        {
            await InstallAsync();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => CancelCurrentOperation();

    private async void SyncPrimaryButton_Click(object sender, RoutedEventArgs e) => await HandlePrimaryActionAsync();

    private async void ReadyPrimaryButton_Click(object sender, RoutedEventArgs e) => await HandlePrimaryActionAsync();

    private void AnnouncementButton_Click(object sender, RoutedEventArgs e) => OpenAnnouncement();

    private async void VerifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_mode == WindowMode.Launcher && !_busy && !_previewMode && !_simulationMode)
        {
            await CheckForUpdatesAsync();
        }
    }

    private async Task PrepareInstallerAsync()
    {
        try
        {
            ValidateInstallPath(InstallPathTextBox.Text);
            SetInstallerStatus("正在诊断更新服务", _bootstrap!.ManifestUri.Host, "");

            var yaml = await DownloadManifestWithDiagnosticsAsync();
            _remoteManifest = new ManifestService().Deserialize(yaml);
            if (string.IsNullOrWhiteSpace(_remoteManifest.BaseUrl))
            {
                _remoteManifest.BaseUrl = new Uri(_bootstrap.ManifestUri, ".").AbsoluteUri.TrimEnd('/');
            }

            _requiredBytes = _remoteManifest.Files.Sum(file => Math.Max(0, file.Size));
            ApplyManifestPresentation(_remoteManifest);
            DownloadSizeText.Text = FormatBytes(_requiredBytes);
            RequiredSpaceText.Text = FormatBytes(GetRequiredFreeSpace());
            SetInstallerStatus(
                "准备就绪",
                _installDryRun ? "模拟模式仅复制启动器并展示更新流程" : "选择安装位置后开始部署",
                _installDryRun ? "warning" : "success");
            DeployButton.IsEnabled = true;
        }
        catch (Exception exception)
        {
            SetInstallerStatus("无法读取安装信息", exception.Message, "error");
            DeployButton.IsEnabled = false;
        }
    }

    private async Task<string> DownloadManifestWithDiagnosticsAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(_bootstrap!.ManifestUri);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"更新服务返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException($"无法连接更新服务（{_bootstrap!.ManifestUri.Host}）：{exception.Message}", exception);
        }
        catch (TaskCanceledException exception) when (!exception.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"连接更新服务超时（超过 {_httpClient.Timeout.TotalSeconds:N0} 秒）。", exception);
        }
    }

    private async Task InstallAsync()
    {
        if (_remoteManifest is null || _bootstrap is null || _dataStore is null)
        {
            return;
        }

        _busy = true;
        DeployButton.IsEnabled = false;
        InstallPathTextBox.IsEnabled = false;
        DesktopShortcutCheckBox.IsEnabled = false;
        SetInstallerStatus("正在准备安装", "正在验证安装目录与磁盘空间", "");
        try
        {
            var targetRoot = ValidateInstallPath(InstallPathTextBox.Text);
            EnsureFreeSpace(targetRoot);
            if (_installDryRun)
            {
                ValidateInstallParent(targetRoot);
                await SimulateInstallAsync(targetRoot);
                return;
            }

            Directory.CreateDirectory(targetRoot);
            EnsureWritable(targetRoot);

            var sourceExecutable = Environment.ProcessPath
                                   ?? throw new InvalidOperationException("无法确定当前启动器路径。");
            var installedLauncher = Path.Combine(targetRoot, "Net.BITKit.Launcher.exe");
            if (!PathSafety.IsSamePath(sourceExecutable, installedLauncher))
            {
                File.Copy(sourceExecutable, installedLauncher, overwrite: true);
            }

            SetInstallerStatus("正在创建安装记录", targetRoot, "");
            await new ManifestService().WriteAtomicAsync(Path.Combine(targetRoot, ManifestService.FileName), _remoteManifest);

            var record = new InstallationRecord(
                _bootstrap.ProductId,
                targetRoot,
                installedLauncher,
                _bootstrap.GameDataDirectory,
                DateTimeOffset.Now);
            _dataStore.Save(record);
            await File.WriteAllTextAsync(
                Path.Combine(targetRoot, InstalledMarkerName),
                JsonSerializer.Serialize(record, new JsonSerializerOptions { WriteIndented = true }));

            var shortcutArguments = $"--config-dir \"{_dataStore.Root}\"";
            ShortcutService.CreateStartMenuShortcut(_bootstrap.DisplayName, installedLauncher, targetRoot, shortcutArguments);
            if (DesktopShortcutCheckBox.IsChecked == true)
            {
                ShortcutService.CreateDesktopShortcut(_bootstrap.DisplayName, installedLauncher, targetRoot, shortcutArguments);
            }

            InstalledPath = targetRoot;
            InstalledLauncherPath = installedLauncher;
            _busy = false;
            DialogResult = true;
        }
        catch (Exception exception)
        {
            _busy = false;
            InstallPathTextBox.IsEnabled = true;
            DesktopShortcutCheckBox.IsEnabled = true;
            DeployButton.IsEnabled = true;
            SetInstallerStatus("安装准备失败", exception.Message, "error");
        }
    }

    private async Task SimulateInstallAsync(string targetRoot)
    {
        SetInstallerStatus("正在创建模拟安装", targetRoot, "warning");
        var sourceExecutable = Environment.ProcessPath
                               ?? throw new InvalidOperationException("无法确定当前启动器路径。");
        Directory.CreateDirectory(targetRoot);
        var installedLauncher = Path.Combine(targetRoot, "Net.BITKit.Launcher.exe");
        if (!PathSafety.IsSamePath(sourceExecutable, installedLauncher))
        {
            File.Copy(sourceExecutable, installedLauncher, overwrite: true);
        }

        await Task.Delay(180);
        SimulationCompleted = true;
        _busy = false;
        DialogResult = true;
    }

    private void ShowPreview()
    {
        SetPhase("install");
        InstallPathTextBox.Text = @"D:\Games\BITFALL";
        DownloadSizeText.Text = "1.55 GiB";
        RequiredSpaceText.Text = "3.20 GiB";
        DeployButtonText.Text = "预览模式";
        DeployButton.IsEnabled = false;
        SetInstallerStatus("界面预览", "联网检查、下载和启动操作均已禁用", "warning");
        VerifyButton.IsEnabled = false;
    }

    private async Task CheckForUpdatesAsync()
    {
        BeginOperation("正在检查更新", indeterminate: true);
        _plan = null;
        _completed = false;
        _primaryMode = "check";
        try
        {
            var progress = new Progress<UpdateProgress>(HandleProgress);
            _updateService = new UpdateService(new ManifestService(), _httpClient, progress: progress);
            _plan = await _updateService.BuildPlanAsync(_workspace, _operationCancellation!.Token);
            ApplyManifestPresentation(_plan.RemoteManifest);

            var changes = _plan.Downloads.Count + _plan.Deletions.Count;
            var bytesTotal = _plan.Downloads.Sum(file => Math.Max(0, file.Entry.Size));
            UpdateProgressVisuals(0, bytesTotal, 0);
            FileCountText.Text = $"文件  {_plan.RemoteManifest.Files.Count}";
            CancelButton.Visibility = Visibility.Collapsed;
            SyncPrimaryButton.IsEnabled = true;
            if (changes == 0)
            {
                SetPhase("ready");
                ReadyStatusText.Text = "全部系统就绪";
                ReadyDescriptionText.Text = "本地资源完整，当前版本与远端部署清单一致。";
                ReadyPrimaryText.Text = _launchButtonText;
                ReadyPrimaryCaption.Text = "LAUNCH APPLICATION";
                _primaryMode = "launch";
            }
            else
            {
                SetPhase("sync");
                SyncStatusText.Text = $"发现 {changes} 项更新";
                SyncDescriptionText.Text = "更新包会在写入应用目录前完成大小与哈希校验。";
                SyncDetailText.Text = $"需要下载 {_plan.Downloads.Count} 个文件，删除 {_plan.Deletions.Count} 个旧文件";
                CurrentFileText.Text = "更新计划已就绪";
                SyncPrimaryText.Text = "获取更新";
                SyncPrimaryCaption.Text = "DOWNLOAD & VERIFY";
                _primaryMode = "update";
            }

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
        if (_mode != WindowMode.Launcher || _busy || _previewMode)
        {
            return;
        }

        if (_completed)
        {
            Close();
            return;
        }

        if (_primaryMode == "check" || _plan is null || _updateService is null)
        {
            await CheckForUpdatesAsync();
            return;
        }

        await ApplyAndLaunchAsync();
    }

    private async Task ApplyAndLaunchAsync()
    {
        if (_plan is null || _updateService is null)
        {
            return;
        }

        IReadOnlyList<BlockingProcess> blockers;
        try
        {
            blockers = RestartManager.GetBlockingProcesses(RestartManager.GetChangedPaths(_plan));
        }
        catch (Exception exception)
        {
            ShowError("无法检查文件占用", exception);
            return;
        }

        if (blockers.Count > 0)
        {
            ShowBlockingProcesses(blockers);
            return;
        }

        BeginOperation(_plan.Downloads.Count == 0 ? "正在准备启动" : "正在下载更新", _plan.Downloads.Count == 0);
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
                SetSyncStatus("正在完成启动器自更新", "窗口关闭后将替换启动器并启动应用", "success");
                UpdateProgressVisuals(1, 1, 100);
                ExitCode = LauncherExitCodes.Success;
                closeAfterOperation = true;
            }
            else
            {
                _completed = true;
                SetPhase("ready");
                ReadyStatusText.Text = "更新完成";
                ReadyDescriptionText.Text = "本地资源已完成校验，应用可以安全启动。";
                ReadyPrimaryText.Text = "关闭";
                ReadyPrimaryCaption.Text = "CLOSE LAUNCHER";
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

        if (closeAfterOperation && IsVisible)
        {
            Close();
        }
    }

    private void HandleProgress(UpdateProgress progress)
    {
        if (!_busy || !IsVisible)
        {
            return;
        }

        _currentStage = progress.Stage;
        SetPhase("sync");
        SyncStatusText.Text = progress.Message.TrimEnd('。');
        if (!string.IsNullOrWhiteSpace(progress.CurrentFile))
        {
            CurrentFileText.Text = progress.CurrentFile;
        }

        var percent = progress.Stage switch
        {
            UpdateStage.Applying or UpdateStage.Completed => 100,
            _ when progress.BytesTotal > 0 => Math.Clamp(progress.BytesCompleted * 100D / progress.BytesTotal, 0, 100),
            _ => 0
        };
        UpdateProgressVisuals(progress.BytesCompleted, progress.BytesTotal, percent);
        FileProgressText.Text = progress.FilesTotal > 0
            ? $"{progress.FilesCompleted}/{progress.FilesTotal}"
            : "--";
        SyncDetailText.Text = progress.FilesTotal > 0
            ? $"文件 {progress.FilesCompleted}/{progress.FilesTotal}"
            : "正在连接安全更新通道";
        CancelButton.Visibility = progress.Stage == UpdateStage.Applying ? Visibility.Collapsed : Visibility.Visible;

        if (progress.Stage == UpdateStage.Downloading)
        {
            _downloadStopwatch ??= Stopwatch.StartNew();
            if (_downloadStopwatch.Elapsed.TotalSeconds > 0.25)
            {
                SpeedText.Text = FormatRate(progress.BytesCompleted / _downloadStopwatch.Elapsed.TotalSeconds);
            }
        }
        else
        {
            SpeedText.Text = "--";
        }
    }

    private void BeginOperation(string message, bool indeterminate)
    {
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _busy = true;
        _currentStage = UpdateStage.Checking;
        _downloadStopwatch = null;
        SetPhase("sync");
        SyncStatusText.Text = message;
        SyncDetailText.Text = indeterminate ? "正在连接安全更新通道" : "正在准备更新";
        CurrentFileText.Text = indeterminate ? "等待更新服务" : "更新计划已就绪";
        SyncPrimaryButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Visible;
        UpdateProgressVisuals(0, 0, 0);
    }

    private void EndOperation()
    {
        _busy = false;
        _operationCancellation?.Dispose();
        _operationCancellation = null;
        _downloadStopwatch = null;
        if (_closeWhenStopped && IsVisible)
        {
            Dispatcher.BeginInvoke(Close);
        }
    }

    private void CancelCurrentOperation()
    {
        if (!_busy || _mode != WindowMode.Launcher)
        {
            return;
        }

        if (_currentStage == UpdateStage.Applying)
        {
            SetSyncStatus("正在应用更新，请稍候", "文件替换阶段不能中断", "warning");
            return;
        }

        SyncStatusText.Text = "正在取消";
        CancelButton.Visibility = Visibility.Collapsed;
        _operationCancellation?.Cancel();
    }

    private void HandleCancellation()
    {
        _primaryMode = "check";
        SetPhase("sync");
        SetSyncStatus("操作已取消", "可以重新检查更新", "warning");
        CurrentFileText.Text = "没有文件被应用";
        SyncPrimaryText.Text = "重新检查";
        SyncPrimaryCaption.Text = "CHECK FOR UPDATES";
        SyncPrimaryButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        ExitCode = LauncherExitCodes.Cancelled;
    }

    private void ShowBlockingProcesses(IReadOnlyList<BlockingProcess> blockers)
    {
        SetPhase("sync");
        SetSyncStatus(
            "请先关闭正在使用更新文件的程序",
            string.Join("  /  ", blockers.Take(4).Select(process => $"{process.Name} (PID {process.ProcessId})")),
            "warning");
        CurrentFileText.Text = "文件正在被其他程序占用";
        SyncPrimaryText.Text = "重新检测并更新";
        SyncPrimaryCaption.Text = "CHECK AGAIN";
        SyncPrimaryButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        _primaryMode = "update";
        ExitCode = LauncherExitCodes.FilesInUse;
    }

    private void ShowError(string title, Exception exception)
    {
        _plan = null;
        _primaryMode = "check";
        SetPhase("sync");
        SetSyncStatus(title, exception.Message, "error");
        CurrentFileText.Text = "操作未完成";
        UpdateProgressVisuals(0, 0, 0);
        SyncPrimaryText.Text = "重新检查";
        SyncPrimaryCaption.Text = "TRY AGAIN";
        SyncPrimaryButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        ExitCode = LauncherExitCodes.Failure;
    }

    private async Task ShowSimulationUpdateAsync()
    {
        ProductNameText.Text = (string.IsNullOrWhiteSpace(_simulationDisplayName) ? "BITKit Game" : _simulationDisplayName).ToUpperInvariant();
        SetPhase("sync");
        SyncDescriptionText.Text = "演示安装后的增量同步过程，不会下载或修改游戏文件。";
        SetSyncStatus("模拟更新准备中", "启动器已复制，正在演示更新流程", "warning");
        SyncPrimaryButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
        TotalBytesText.Text = " / 1.55 GiB";

        var sampleFiles = new[] { "ProjectB.exe", "UnityPlayer.dll", "GameAssembly.dll", "StreamingAssets", "resources.assets" };
        const long bytesTotal = 1587L * 1024 * 1024;
        for (var percent = 0; percent <= 100; percent += 2)
        {
            await Task.Delay(35);
            if (!IsVisible)
            {
                return;
            }

            SyncStatusText.Text = $"模拟更新 {percent}%";
            CurrentFileText.Text = sampleFiles[Math.Min(sampleFiles.Length - 1, percent * sampleFiles.Length / 101)];
            UpdateProgressVisuals((long)(bytesTotal * percent / 100D), bytesTotal, percent);
        }

        SetPhase("ready");
        ReadyStatusText.Text = "模拟更新完成";
        ReadyDescriptionText.Text = "演示结束，未下载或修改任何游戏文件。";
        ReadyPrimaryText.Text = "关闭";
        ReadyPrimaryCaption.Text = "CLOSE PREVIEW";
        _completed = true;
    }

    private void ApplyManifestPresentation(Manifest manifest)
    {
        var productName = !string.IsNullOrWhiteSpace(_simulationDisplayName)
            ? _simulationDisplayName
            : manifest.Name;
        if (!string.IsNullOrWhiteSpace(productName))
        {
            ProductNameText.Text = productName.ToUpperInvariant();
        }

        var version = string.IsNullOrWhiteSpace(manifest.Version) ? "--" : manifest.Version;
        BuildText.Text = version == "--" ? "BUILD READY" : $"BUILD {version}";
        VersionText.Text = $"版本  {version}";
        FileCountText.Text = $"文件  {manifest.Files.Count}";
        AnnouncementTitleText.Text = string.IsNullOrWhiteSpace(manifest.Announcement?.Title)
            ? "行动简报"
            : manifest.Announcement.Title;
        AnnouncementContentText.Text = string.IsNullOrWhiteSpace(manifest.Announcement?.Content)
            ? "启动器会保持应用为最新版本。"
            : manifest.Announcement.Content;
        _announcementUrl = manifest.Announcement?.Url;
        _launchButtonText = string.IsNullOrWhiteSpace(manifest.Ui?.ButtonText)
            ? "开始行动"
            : manifest.Ui.ButtonText;
        TrySetAccentColor(manifest.Ui?.AccentColor);

        if (_mode == WindowMode.Launcher && !string.IsNullOrWhiteSpace(manifest.Ui?.Background))
        {
            try
            {
                var path = PathSafety.ResolveUnderRoot(_workspace, manifest.Ui.Background);
                if (File.Exists(path))
                {
                    SetArtwork(path);
                }
            }
            catch
            {
                // Invalid presentation metadata must not affect update behavior.
            }
        }
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

    private void OpenAnnouncement()
    {
        if (!IsHttpUrl(_announcementUrl))
        {
            MessageBox.Show(this, "当前没有可打开的公告链接。", "BITFALL", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        Process.Start(new ProcessStartInfo { FileName = _announcementUrl!, UseShellExecute = true });
    }

    private void SetPhase(string phase)
    {
        InstallHero.Visibility = phase == "install" ? Visibility.Visible : Visibility.Collapsed;
        SyncHero.Visibility = phase == "sync" ? Visibility.Visible : Visibility.Collapsed;
        ReadyHero.Visibility = phase == "ready" ? Visibility.Visible : Visibility.Collapsed;
        InstallPanel.Visibility = phase == "install" ? Visibility.Visible : Visibility.Collapsed;
        SyncPanel.Visibility = phase == "sync" ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = phase == "ready" ? Visibility.Visible : Visibility.Collapsed;
        SetPhaseButton(InstallPhaseButton, InstallPhaseLine, phase == "install");
        SetPhaseButton(SyncPhaseButton, SyncPhaseLine, phase == "sync");
        SetPhaseButton(ReadyPhaseButton, ReadyPhaseLine, phase == "ready");
    }

    private static void SetPhaseButton(System.Windows.Controls.Button button, FrameworkElement line, bool active)
    {
        button.Tag = active ? "Active" : null;
        line.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetInstallerStatus(string status, string detail, string tone)
    {
        InstallStatusText.Text = status;
        InstallDetailText.Text = detail;
        InstallStatusText.Foreground = ToneBrush(tone);
    }

    private void SetSyncStatus(string status, string detail, string tone)
    {
        SyncStatusText.Text = status;
        SyncDetailText.Text = detail;
        SyncStatusText.Foreground = ToneBrush(tone);
    }

    private static Brush ToneBrush(string tone) => tone switch
    {
        "error" => new SolidColorBrush(Color.FromRgb(224, 116, 100)),
        "warning" => new SolidColorBrush(Color.FromRgb(224, 184, 98)),
        "success" => new SolidColorBrush(Color.FromRgb(182, 212, 155)),
        _ => new SolidColorBrush(Color.FromRgb(164, 168, 158))
    };

    private void UpdateProgressVisuals(long completed, long total, double percent)
    {
        var normalized = Math.Clamp(percent, 0, 100);
        ProgressValueText.Text = $"{normalized:N0}%";
        ProgressFill.Width = 401 * normalized / 100D;
        DownloadedText.Text = FormatBytes(completed);
        TotalBytesText.Text = total > 0 ? $" / {FormatBytes(total)}" : " / --";
    }

    private void LoadEmbeddedArtwork()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("BITKit.LauncherArtwork");
            if (stream is not null)
            {
                HeroImage.Source = LoadBitmap(stream);
            }
        }
        catch
        {
            // Artwork is optional; the solid background remains usable.
        }
    }

    private void SetArtwork(string path)
    {
        using var stream = File.OpenRead(path);
        HeroImage.Source = LoadBitmap(stream);
    }

    private static BitmapImage LoadBitmap(Stream stream)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.StreamSource = stream;
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void TrySetAccentColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length != 7
            || value[0] != '#'
            || !value.Skip(1).All(Uri.IsHexDigit))
        {
            return;
        }

        try
        {
            if (FindResource("AccentBrush") is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = (Color)ColorConverter.ConvertFromString(value);
            }
        }
        catch
        {
            // Invalid presentation metadata must not affect update behavior.
        }
    }

    private string RecommendInstallPath() => Path.Combine(@"D:\Games", _bootstrap!.InstallDirectoryName);

    private static string ValidateInstallPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException("请选择安装目录。");
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(path.Trim());
        if (expandedPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new InvalidOperationException("安装目录包含 Windows 不允许的字符。请重新选择目录。");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(expandedPath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException("安装目录格式无效。请重新选择目录。", exception);
        }

        var leaf = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf) || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidOperationException("安装目录名称包含 Windows 不允许的字符。请重新选择目录。");
        }

        if (string.Equals(Path.GetPathRoot(fullPath), fullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能直接安装到磁盘根目录。");
        }

        if (File.Exists(fullPath))
        {
            throw new InvalidOperationException("安装路径已经是一个文件。请选择目录。");
        }

        return fullPath;
    }

    private void EnsureFreeSpace(string targetRoot)
    {
        var root = Path.GetPathRoot(targetRoot) ?? throw new InvalidOperationException("无法确定安装磁盘。");
        var drive = new DriveInfo(root);
        if (!drive.IsReady || drive.AvailableFreeSpace < GetRequiredFreeSpace())
        {
            throw new IOException($"磁盘空间不足，至少需要 {FormatBytes(GetRequiredFreeSpace())} 可用空间。");
        }
    }

    private long GetRequiredFreeSpace() => _requiredBytes + Math.Max(512L * 1024 * 1024, _requiredBytes / 10);

    private static void EnsureWritable(string directory)
    {
        var probe = Path.Combine(directory, ".bitkit-write-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static void ValidateInstallParent(string targetRoot)
    {
        var parent = Directory.Exists(targetRoot)
            ? targetRoot
            : Directory.GetParent(targetRoot)?.FullName
              ?? throw new InvalidOperationException("无法确定安装目录的父目录。");
        while (!Directory.Exists(parent))
        {
            parent = Directory.GetParent(parent)?.FullName
                     ?? throw new InvalidOperationException("无法确定可写的安装父目录。");
        }

        EnsureWritable(parent);
    }

    private static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string FormatRate(double bytesPerSecond) => $"{FormatBytes((long)Math.Max(0, bytesPerSecond))}/s";

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024D * 1024 * 1024):N2} GiB",
        >= 1024L * 1024 => $"{bytes / (1024D * 1024):N1} MiB",
        _ => $"{bytes / 1024D:N0} KiB"
    };

    private enum WindowMode
    {
        Installer,
        Launcher
    }
}

internal static class ShortcutService
{
    public static void CreateDesktopShortcut(string name, string target, string workingDirectory, string arguments) =>
        Create(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk"),
            target,
            workingDirectory,
            arguments);

    public static void CreateStartMenuShortcut(string name, string target, string workingDirectory, string arguments)
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
        Create(Path.Combine(programs, name, name + ".lnk"), target, workingDirectory, arguments);
    }

    private static void Create(string shortcutPath, string target, string workingDirectory, string arguments)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
                        ?? throw new PlatformNotSupportedException("Windows 快捷方式服务不可用。");
        dynamic? shell = null;
        dynamic? shortcut = null;
        try
        {
            shell = Activator.CreateInstance(shellType);
            shortcut = shell!.CreateShortcut(shortcutPath);
            shortcut.TargetPath = target;
            shortcut.WorkingDirectory = workingDirectory;
            shortcut.Arguments = arguments;
            shortcut.IconLocation = target + ",0";
            shortcut.Save();
        }
        finally
        {
            if (shortcut is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
            }

            if (shell is not null)
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
    }
}
