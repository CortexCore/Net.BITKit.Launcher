using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Net.BITKit.Launcher;
using Net.BITKit.Launcher.WinForm.Controls;

namespace Net.BITKit.Launcher.WinForm;

internal sealed class MiniInstallerForm : Form
{
    private const string InstalledMarkerName = ".bitkit-install.json";
    private readonly BootstrapConfiguration _bootstrap;
    private readonly LauncherDataStore _dataStore;
    private readonly TextBox _installPath = new();
    private readonly Label _spaceLabel = new();
    private readonly Label _statusLabel = new();
    private readonly CheckBox _desktopShortcut = new();
    private readonly PrimaryButton _installButton = new();
    private readonly Button _customizeButton = new();
    private readonly Button _browseButton = new();
    private readonly ProgressBar _progressBar = new();
    private readonly Label _modeLabel = new();
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly bool _dryRun;
    private Manifest? _remoteManifest;
    private long _requiredBytes;

    public MiniInstallerForm(BootstrapConfiguration bootstrap, LauncherDataStore dataStore, bool dryRun = false)
    {
        _bootstrap = bootstrap;
        _dataStore = dataStore;
        _dryRun = dryRun;
        InitializeLayout();
        Shown += async (_, _) => await PrepareAsync();
    }

    public string? InstalledPath { get; private set; }
    public string? InstalledLauncherPath { get; private set; }
    public bool SimulationCompleted { get; private set; }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _httpClient.Dispose();
        }

        base.Dispose(disposing);
    }

    private void InitializeLayout()
    {
        Text = (_dryRun ? "[模拟] " : "") + $"安装 {_bootstrap.DisplayName}";
        Icon = TryLoadApplicationIcon();
        ClientSize = new Size(760, 520);
        MinimumSize = MaximumSize = Size;
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.None;
        MaximizeBox = false;
        BackColor = Color.FromArgb(7, 14, 28);
        Font = new Font("Segoe UI", 9.5F);

        var canvas = new HeroCanvas
        {
            Dock = DockStyle.Fill,
            Artwork = LoadEmbeddedArtwork()
        };
        Controls.Add(canvas);

        var closeButton = new Button
        {
            Text = "×",
            FlatStyle = FlatStyle.Flat,
            FlatAppearance = { BorderSize = 0 },
            ForeColor = Color.White,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 16F),
            Size = new Size(42, 36),
            Location = new Point(ClientSize.Width - 48, 8),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Cursor = Cursors.Hand
        };
        closeButton.Click += (_, _) => Close();
        canvas.Controls.Add(closeButton);

        var card = new GlassPanel
        {
            Location = new Point(36, 64),
            Size = new Size(688, 410),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            Padding = new Padding(28, 22, 28, 22)
        };
        card.FillColor = Color.FromArgb(80, 5, 12, 28);
        card.BorderColor = Color.Transparent;
        card.CornerRadius = 0;
        canvas.Controls.Add(card);

        _modeLabel.AutoSize = true;
        _modeLabel.Location = new Point(470, 25);
        _modeLabel.Size = new Size(188, 24);
        _modeLabel.TextAlign = ContentAlignment.MiddleRight;
        _modeLabel.Text = _dryRun ? "模拟安装 · 仅复制启动器" : "首次安装";
        _modeLabel.Font = new Font("Segoe UI", 8.5F, FontStyle.Bold);
        _modeLabel.ForeColor = _dryRun ? Color.FromArgb(255, 210, 62) : Color.FromArgb(180, 215, 230);
        _modeLabel.BackColor = Color.Transparent;
        card.Controls.Add(_modeLabel);
        _modeLabel.Visible = false;

        var title = new Label
        {
            AutoSize = false,
            Location = new Point(28, 20),
            Size = new Size(620, 50),
            Text = $"安装 {_bootstrap.DisplayName}",
            Font = new Font("Segoe UI", 18F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        card.Controls.Add(title);
        title.Visible = false;

        var description = new Label
        {
            AutoSize = false,
            Location = new Point(30, 62),
            Size = new Size(620, 52),
            Text = "选择游戏安装位置。启动器、游戏文件与个人数据彼此独立。",
            ForeColor = Color.FromArgb(210, 226, 238),
            BackColor = Color.Transparent
        };
        card.Controls.Add(description);

        var pathCaption = new Label
        {
            AutoSize = true,
            Location = new Point(30, 113),
            Text = "游戏安装目录",
            Font = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = Color.White,
            BackColor = Color.Transparent
        };
        card.Controls.Add(pathCaption);

        _installPath.Location = new Point(30, 140);
        _installPath.Size = new Size(530, 29);
        _installPath.BorderStyle = BorderStyle.FixedSingle;
        card.Controls.Add(_installPath);

        _browseButton.SuspendLayout();
        _browseButton.Location = new Point(570, 139);
        _browseButton.Size = new Size(88, 31);
        _browseButton.Text = "浏览…";
        _browseButton.FlatStyle = FlatStyle.System;
        _browseButton.Click += (_, _) => BrowseInstallPath();
        card.Controls.Add(_browseButton);
        _browseButton.ResumeLayout();

        var browseButton = new Button
        {
            Location = new Point(570, 139),
            Size = new Size(88, 31),
            Text = "浏览…",
            FlatStyle = FlatStyle.System
        };
        browseButton.Visible = false;

        _spaceLabel.AutoSize = false;
        _spaceLabel.Location = new Point(30, 178);
        _spaceLabel.Size = new Size(628, 23);
        _spaceLabel.ForeColor = Color.FromArgb(196, 211, 225, 240);
        _spaceLabel.BackColor = Color.Transparent;
        _spaceLabel.Text = "正在读取安装信息…";
        card.Controls.Add(_spaceLabel);

        var gameDataLabel = new Label
        {
            AutoSize = false,
            Location = new Point(30, 207),
            Size = new Size(628, 24),
            AutoEllipsis = true,
            Text = string.IsNullOrWhiteSpace(_bootstrap.GameDataDirectory)
                ? "游戏数据目录：由游戏决定"
                : $"游戏数据目录：{_bootstrap.GameDataDirectory}",
            ForeColor = Color.FromArgb(196, 211, 225, 240),
            BackColor = Color.Transparent
        };
        card.Controls.Add(gameDataLabel);
        gameDataLabel.Visible = false;

        _desktopShortcut.AutoSize = true;
        _desktopShortcut.Location = new Point(30, 241);
        _desktopShortcut.Text = "创建桌面快捷方式";
        _desktopShortcut.Checked = true;
        _desktopShortcut.ForeColor = Color.White;
        _desktopShortcut.BackColor = Color.Transparent;
        card.Controls.Add(_desktopShortcut);

        _customizeButton.Location = new Point(430, 204);
        _customizeButton.Size = new Size(112, 36);
        _customizeButton.Text = "自定义安装…";
        _customizeButton.FlatStyle = FlatStyle.Flat;
        _customizeButton.FlatAppearance.BorderSize = 1;
        _customizeButton.FlatAppearance.BorderColor = Color.FromArgb(255, 210, 62);
        _customizeButton.ForeColor = Color.FromArgb(255, 210, 62);
        _customizeButton.Font = new Font("Segoe UI", 9F);
        _customizeButton.TextAlign = ContentAlignment.MiddleCenter;
        _customizeButton.UseVisualStyleBackColor = false;
        _customizeButton.Text = "自定义安装…";
        _customizeButton.Click += (_, _) => ShowAdvancedOptions(card, description, pathCaption, gameDataLabel);
        card.Controls.Add(_customizeButton);

        _statusLabel.AutoSize = false;
        _statusLabel.Location = new Point(30, 276);
        _statusLabel.Size = new Size(410, 40);
        _statusLabel.Text = "准备安装…";
        _statusLabel.ForeColor = Color.FromArgb(215, 226, 238);
        _statusLabel.BackColor = Color.Transparent;
        card.Controls.Add(_statusLabel);

        _progressBar.Location = new Point(30, 320);
        _progressBar.Size = new Size(410, 12);
        _progressBar.Minimum = 0;
        _progressBar.Maximum = 100;
        _progressBar.Value = 0;
        _progressBar.Style = ProgressBarStyle.Continuous;
        _progressBar.Visible = false;
        card.Controls.Add(_progressBar);

        _installButton.Location = new Point(468, 270);
        _installButton.Size = new Size(190, 52);
        _installButton.Text = _dryRun ? "模拟安装" : "安装游戏";
        _installButton.Font = new Font("Segoe UI", 11F, FontStyle.Bold);
        _installButton.PrimaryColor = Color.FromArgb(255, 205, 55);
        _installButton.HoverColor = Color.FromArgb(255, 218, 91);
        _installButton.PressedColor = Color.FromArgb(232, 180, 31);
        _installButton.ForeColor = Color.FromArgb(15, 23, 42);
        _installButton.CornerRadius = 0;
        _installButton.Enabled = false;
        _installButton.Click += async (_, _) => await InstallAsync();
        card.Controls.Add(_installButton);

        description.Visible = false;
        pathCaption.Visible = false;
        _installPath.Visible = false;
        _browseButton.Visible = false;
        _spaceLabel.Visible = false;
        gameDataLabel.Visible = false;
        _desktopShortcut.Visible = false;
        _statusLabel.Visible = false;
        _installButton.Size = new Size(285, 78);
        _installButton.Location = new Point(201, 166);
        _customizeButton.Location = new Point(374, 246);
    }

    private void ShowAdvancedOptions(GlassPanel card, Control description, Control pathCaption, Control gameDataLabel)
    {
        var layout = new FlowLayoutPanel
        {
            Location = new Point(24, 18),
            Size = new Size(card.ClientSize.Width - 48, card.ClientSize.Height - 36),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(4)
        };
        foreach (var control in new Control[] { description, pathCaption, _installPath, _browseButton, _spaceLabel, gameDataLabel, _desktopShortcut, _statusLabel, _installButton })
        {
            card.Controls.Remove(control);
            layout.Controls.Add(control);
        }
        card.Controls.Add(layout);
        layout.BringToFront();
        card.FillColor = Color.FromArgb(225, 5, 12, 28);
        card.BorderColor = Color.FromArgb(110, 255, 255, 255);
        card.CornerRadius = 6;
        card.Invalidate();
        _modeLabel.Visible = true;
        var title = card.Controls.OfType<Label>().FirstOrDefault(label => label.Font.Size >= 18F);
        if (title is not null)
        {
            title.Visible = true;
        }
        description.Visible = true;
        pathCaption.Visible = true;
        _installPath.Visible = true;
        _browseButton.Visible = true;
        _spaceLabel.Visible = true;
        gameDataLabel.Visible = !string.IsNullOrWhiteSpace(_bootstrap.GameDataDirectory);
        _desktopShortcut.Visible = true;
        _customizeButton.Visible = false;
        _statusLabel.Visible = true;
        _statusLabel.Location = new Point(30, 276);
        _installButton.Size = new Size(190, 52);
        _installButton.Location = new Point(468, 270);
    }

    private async Task PrepareAsync()
    {
        try
        {
            _installPath.Text = RecommendInstallPath();
            ValidateInstallPath(_installPath.Text);
            _statusLabel.Text = "正在诊断更新服务…";
            var yaml = await DownloadManifestWithDiagnosticsAsync();
            _remoteManifest = new ManifestService().Deserialize(yaml);
            if (string.IsNullOrWhiteSpace(_remoteManifest.BaseUrl))
            {
                _remoteManifest.BaseUrl = new Uri(_bootstrap.ManifestUri, ".").AbsoluteUri.TrimEnd('/');
            }

            _requiredBytes = _remoteManifest.Files.Sum(file => Math.Max(0, file.Size));
            _spaceLabel.Text = $"需要下载约 {FormatBytes(_requiredBytes)}；安装期间建议预留 {FormatBytes(GetRequiredFreeSpace())}";
            _statusLabel.Text = "准备就绪";
            _installButton.Enabled = true;
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "无法读取安装信息：" + exception.Message;
            _statusLabel.ForeColor = Color.FromArgb(255, 165, 165);
        }
    }

    private async Task<string> DownloadManifestWithDiagnosticsAsync()
    {
        try
        {
            using var response = await _httpClient.GetAsync(_bootstrap.ManifestUri);
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"更新服务返回 HTTP {(int)response.StatusCode} {response.ReasonPhrase}。");
            }

            return await response.Content.ReadAsStringAsync();
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidOperationException(
                $"无法连接更新服务（{_bootstrap.ManifestUri.Host}）：{exception.Message}",
                exception);
        }
        catch (TaskCanceledException exception) when (!exception.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"连接更新服务超时（超过 {_httpClient.Timeout.TotalSeconds:N0} 秒）。",
                exception);
        }
    }

    private async Task InstallAsync()
    {
        if (_remoteManifest is null)
        {
            return;
        }

        _installButton.Enabled = false;
        _installPath.Enabled = false;
        _statusLabel.ForeColor = Color.FromArgb(215, 226, 238);
        try
        {
            var targetRoot = ValidateInstallPath(_installPath.Text);
            EnsureFreeSpace(targetRoot);
            if (_dryRun)
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

            _statusLabel.Text = "正在创建安装记录…";
            var manifestService = new ManifestService();
            await manifestService.WriteAtomicAsync(
                Path.Combine(targetRoot, ManifestService.FileName),
                _remoteManifest);

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
            ShortcutService.CreateStartMenuShortcut(
                _bootstrap.DisplayName,
                installedLauncher,
                targetRoot,
                shortcutArguments);
            if (_desktopShortcut.Checked)
            {
                ShortcutService.CreateDesktopShortcut(
                    _bootstrap.DisplayName,
                    installedLauncher,
                    targetRoot,
                    shortcutArguments);
            }

            InstalledPath = targetRoot;
            InstalledLauncherPath = installedLauncher;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception)
        {
            _statusLabel.Text = "安装准备失败：" + exception.Message;
            _statusLabel.ForeColor = Color.FromArgb(255, 165, 165);
            _installButton.Enabled = true;
            _installPath.Enabled = true;
        }
    }

    private async Task SimulateInstallAsync(string targetRoot)
    {
        _installButton.Enabled = false;
        _installPath.Enabled = false;
        _desktopShortcut.Enabled = false;
        _statusLabel.ForeColor = Color.FromArgb(255, 210, 62);
        var sourceExecutable = Environment.ProcessPath
                               ?? throw new InvalidOperationException("Unable to determine current launcher path.");
        Directory.CreateDirectory(targetRoot);
        var installedLauncher = Path.Combine(targetRoot, "Net.BITKit.Launcher.exe");
        if (!PathSafety.IsSamePath(sourceExecutable, installedLauncher))
        {
            File.Copy(sourceExecutable, installedLauncher, overwrite: true);
        }

        await Task.Delay(180);
        SimulationCompleted = true;
        DialogResult = DialogResult.OK;
        Close();
        if (sourceExecutable.Length >= 0)
        {
            return;
        }

        /* Legacy fake-download body retained below for reference. */
        _installButton.Enabled = false;
        _installPath.Enabled = false;
        _desktopShortcut.Enabled = false;
        _progressBar.Value = 0;
        _statusLabel.ForeColor = Color.FromArgb(255, 210, 62);
        _statusLabel.Text = "模拟下载：不会写入任何文件…";

        var files = _remoteManifest?.Files ?? [];
        var total = Math.Max(1L, files.Sum(file => Math.Max(0, file.Size)));
        long completed = 0;
        foreach (var file in files)
        {
            await Task.Delay(12);
            completed += Math.Max(0, file.Size);
            _progressBar.Value = (int)Math.Clamp(completed * 100L / total, 0, 100);
            _statusLabel.Text = $"模拟下载 {_progressBar.Value}%：{Path.GetFileName(file.Path)}";
        }

        _progressBar.Value = 100;
        _statusLabel.ForeColor = Color.FromArgb(170, 235, 185);
        _statusLabel.Text = $"模拟通过：将安装到 {targetRoot}；未写入文件，未创建快捷方式。";
        SimulationCompleted = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void BrowseInstallPath()
    {
        using var dialog = new OpenFileDialog
        {
            Title = $"选择 {_bootstrap.DisplayName} 的安装目录",
            CheckFileExists = false,
            CheckPathExists = true,
            ValidateNames = false,
            FileName = "选择此文件夹",
            InitialDirectory = Directory.Exists(_installPath.Text)
                ? _installPath.Text
                : Path.GetDirectoryName(_installPath.Text)
        };
        if (dialog.ShowDialog(this) == DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.FileName))
        {
            var selectedDirectory = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(selectedDirectory))
            {
                _installPath.Text = Path.Combine(selectedDirectory, _bootstrap.InstallDirectoryName);
            }
        }
    }

    private string RecommendInstallPath()
    {
        var currentDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
        if (IsSuitableCurrentDirectory(currentDirectory))
        {
            return currentDirectory;
        }

        var systemRoot = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        var dataDrive = DriveInfo.GetDrives()
            .Where(drive => drive.IsReady && drive.DriveType == DriveType.Fixed)
            .Where(drive => !string.Equals(drive.RootDirectory.FullName, systemRoot, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(drive => drive.AvailableFreeSpace)
            .FirstOrDefault();
        return dataDrive is not null
            ? Path.Combine(dataDrive.RootDirectory.FullName, "Games", _bootstrap.InstallDirectoryName)
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                _bootstrap.InstallDirectoryName);
    }

    private static bool IsSuitableCurrentDirectory(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var unsuitableRoots = new[]
        {
            Path.GetTempPath(),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }.Where(value => !string.IsNullOrWhiteSpace(value));
        if (unsuitableRoots.Any(root => PathSafety.IsUnderRoot(root, fullPath)))
        {
            return false;
        }

        var pathRoot = Path.GetPathRoot(fullPath);
        if (string.Equals(pathRoot, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return Directory.EnumerateFileSystemEntries(fullPath).Take(3).Count() <= 2;
    }

    private static string ValidateInstallPath(string path)
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
        if (string.IsNullOrWhiteSpace(leaf)
            || leaf.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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
        var root = Path.GetPathRoot(targetRoot)
                   ?? throw new InvalidOperationException("无法确定安装磁盘。");
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

    private static Image? LoadEmbeddedArtwork()
    {
        const string resourceName = "Net.BITKit.Launcher.WinForm.Assets.default-hero.png";
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            return null;
        }

        using var image = Image.FromStream(stream);
        return new Bitmap(image);
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

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / (1024D * 1024 * 1024):N2} GiB",
        >= 1024L * 1024 => $"{bytes / (1024D * 1024):N1} MiB",
        _ => $"{bytes / 1024D:N0} KiB"
    };
}

internal static class ShortcutService
{
    public static void CreateDesktopShortcut(
        string name,
        string target,
        string workingDirectory,
        string arguments) =>
        Create(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".lnk"),
            target,
            workingDirectory,
            arguments);

    public static void CreateStartMenuShortcut(
        string name,
        string target,
        string workingDirectory,
        string arguments)
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
