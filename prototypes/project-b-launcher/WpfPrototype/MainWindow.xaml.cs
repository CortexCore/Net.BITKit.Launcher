using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ProjectB.LauncherWpfPrototype;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _simulationCancellation;

    public MainWindow()
    {
        InitializeComponent();
        SetPhase("install");
    }

    public void SetPhase(string phase)
    {
        var normalized = phase.ToLowerInvariant();
        InstallHero.Visibility = normalized == "install" ? Visibility.Visible : Visibility.Collapsed;
        SyncHero.Visibility = normalized == "sync" ? Visibility.Visible : Visibility.Collapsed;
        ReadyHero.Visibility = normalized == "ready" ? Visibility.Visible : Visibility.Collapsed;
        InstallPanel.Visibility = normalized == "install" ? Visibility.Visible : Visibility.Collapsed;
        SyncPanel.Visibility = normalized == "sync" ? Visibility.Visible : Visibility.Collapsed;
        ReadyPanel.Visibility = normalized == "ready" ? Visibility.Visible : Visibility.Collapsed;

        SetPhaseButton(InstallPhaseButton, InstallPhaseLine, normalized == "install");
        SetPhaseButton(SyncPhaseButton, SyncPhaseLine, normalized == "sync");
        SetPhaseButton(ReadyPhaseButton, ReadyPhaseLine, normalized == "ready");
    }

    public void RenderToPng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var dpi = VisualTreeHelper.GetDpi(this);
        var bitmap = new RenderTargetBitmap(
            (int)Math.Round(ActualWidth * dpi.DpiScaleX),
            (int)Math.Round(ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(this);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    protected override void OnClosed(EventArgs e)
    {
        _simulationCancellation?.Cancel();
        _simulationCancellation?.Dispose();
        Application.Current.Shutdown();
        base.OnClosed(e);
    }

    private static void SetPhaseButton(System.Windows.Controls.Button button, FrameworkElement line, bool active)
    {
        button.Tag = active ? "Active" : null;
        line.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }

        DragMove();
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

    private void InstallPhaseButton_Click(object sender, RoutedEventArgs e) => SetPhase("install");

    private void SyncPhaseButton_Click(object sender, RoutedEventArgs e) => SetPhase("sync");

    private void ReadyPhaseButton_Click(object sender, RoutedEventArgs e) => SetPhase("ready");

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择 BITFALL 安装目录",
            InitialDirectory = Directory.Exists(InstallPathTextBox.Text)
                ? InstallPathTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog(this) == true)
        {
            InstallPathTextBox.Text = dialog.FolderName;
        }
    }

    private async void DeployButton_Click(object sender, RoutedEventArgs e)
    {
        SetPhase("sync");
        _simulationCancellation?.Cancel();
        _simulationCancellation?.Dispose();
        _simulationCancellation = new CancellationTokenSource();
        try
        {
            var files = new[]
            {
                "Project B_Data/globalgamemanagers",
                "Project B_Data/resources.assets",
                "Project B_Data/sharedassets0.assets",
                "UnityPlayer.dll"
            };
            for (var progress = 4; progress <= 100; progress += 2)
            {
                _simulationCancellation.Token.ThrowIfCancellationRequested();
                ProgressValueText.Text = $"{progress}%";
                ProgressFill.Width = 401 * progress / 100D;
                DownloadedText.Text = $"{progress * 15.5:N0} MiB";
                CurrentFileText.Text = files[Math.Min(files.Length - 1, progress / 26)];
                await Task.Delay(55, _simulationCancellation.Token);
            }

            SetPhase("ready");
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CompleteSyncButton_Click(object sender, RoutedEventArgs e) => SetPhase("ready");

    private void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this, "WPF 原生界面已就绪。此原型未连接实际游戏进程。", "BITFALL", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
