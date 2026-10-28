using System.IO;
using System.Windows;

namespace ProjectB.LauncherWpfPrototype;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var window = new MainWindow();
        var renderIndex = Array.FindIndex(e.Args, argument =>
            argument.Equals("--render", StringComparison.OrdinalIgnoreCase));
        if (renderIndex >= 0 && renderIndex + 1 < e.Args.Length)
        {
            var phase = renderIndex + 2 < e.Args.Length ? e.Args[renderIndex + 2] : "install";
            window.SetPhase(phase);
            window.Show();
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            window.RenderToPng(Path.GetFullPath(e.Args[renderIndex + 1]));
            window.Close();
            Shutdown();
            return;
        }

        MainWindow = window;
        window.Show();
    }
}
