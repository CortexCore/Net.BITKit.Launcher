using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ProjectB.LauncherPrototype;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new PrototypeForm());
    }
}

internal sealed class PrototypeForm : Form
{
    private readonly WebView2 _webView = new() { Dock = DockStyle.Fill };

    public PrototypeForm()
    {
        Text = "Project B Launcher Concept";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.CenterScreen;
        ShowIcon = false;
        MaximizeBox = false;
        MinimizeBox = false;
        BackColor = Color.FromArgb(8, 10, 8);
        AutoScaleMode = AutoScaleMode.Dpi;
        SetWindowSize();
        Controls.Add(_webView);
        Shown += OnShown;
    }

    private void SetWindowSize()
    {
        var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        var width = Math.Min(1920, workArea.Width - 96);
        var height = (int)Math.Round(width * 9D / 16D);
        if (height > workArea.Height - 96)
        {
            height = workArea.Height - 96;
            width = (int)Math.Round(height * 16D / 9D);
        }

        ClientSize = new Size(width, height);
    }

    private async void OnShown(object? sender, EventArgs eventArgs)
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BITKit",
                "ProjectBLauncherPrototype");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userData);
            await _webView.EnsureCoreWebView2Async(environment);

            var settings = _webView.CoreWebView2.Settings;
            settings.AreBrowserAcceleratorKeysEnabled = false;
            settings.AreDefaultContextMenusEnabled = false;
            settings.AreDevToolsEnabled = false;
            settings.IsStatusBarEnabled = false;
            settings.IsZoomControlEnabled = false;
            _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            _webView.Source = new Uri(Path.Combine(AppContext.BaseDirectory, "index.html"));
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"无法打开视觉原型：{exception.Message}",
                "Project B Launcher Concept",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            Close();
        }
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs eventArgs)
    {
        using var message = JsonDocument.Parse(eventArgs.TryGetWebMessageAsString());
        if (!message.RootElement.TryGetProperty("action", out var actionElement))
        {
            return;
        }

        switch (actionElement.GetString())
        {
            case "close":
                Close();
                break;
            case "minimize":
                WindowState = FormWindowState.Minimized;
                break;
            case "drag":
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(Handle, 0x00A1, new IntPtr(2), IntPtr.Zero);
                break;
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    internal static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
