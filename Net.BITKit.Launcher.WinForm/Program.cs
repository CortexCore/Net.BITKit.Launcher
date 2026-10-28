using System.Diagnostics;
using System.Windows;
using Net.BITKit.Launcher;

namespace Net.BITKit.Launcher.WinForm;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.FirstOrDefault() == SelfUpdater.ApplyCommand)
        {
            return SelfUpdater.ApplyAsync(args).GetAwaiter().GetResult();
        }

        if (args.FirstOrDefault() == SelfUpdater.CleanupCommand)
        {
            return SelfUpdater.CleanupAsync(args).GetAwaiter().GetResult();
        }

        var startup = ParseStartupArguments(args);
        var previewMode = startup.Arguments.Count == 1
                          && string.Equals(startup.Arguments[0], "--ui-preview", StringComparison.OrdinalIgnoreCase);
        var firstInstall = startup.Arguments.Count == 1
                           && string.Equals(startup.Arguments[0], "--first-install", StringComparison.OrdinalIgnoreCase);
        var installDryRun = startup.Arguments.Count == 1
                            && (string.Equals(startup.Arguments[0], "--simulate-install", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(startup.Arguments[0], "--install-dry-run", StringComparison.OrdinalIgnoreCase));
        if (startup.Arguments.Count > 0 && !previewMode && !firstInstall && !installDryRun)
        {
            return LauncherApplication.RunAsync(startup.Arguments.ToArray()).GetAwaiter().GetResult();
        }

        ConsoleWindow.HideIfOwnedByCurrentProcess();
        var workspace = Path.GetFullPath(Environment.CurrentDirectory);
        var bootstrap = BootstrapConfiguration.Load();
        var dataStore = new LauncherDataStore(startup.ConfigDirectory);
        if (installDryRun && bootstrap is null)
        {
            MessageBox.Show(
                "此启动器没有内嵌安装配置，无法模拟安装。请使用专用发布版本。",
                "Net.BITKit.Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return LauncherExitCodes.InvalidArguments;
        }
        using var instanceLock = SingleInstanceLock.TryAcquire(workspace);
        if (instanceLock is null)
        {
            MessageBox.Show(
                "此目录中的启动器已经在运行。",
                "Net.BITKit.Launcher",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return LauncherExitCodes.AlreadyRunning;
        }

        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        try
        {
            if (!previewMode
                && !firstInstall
                && bootstrap is not null
                && !File.Exists(Path.Combine(workspace, ManifestService.FileName)))
            {
                var existing = dataStore.Load(bootstrap.ProductId);
                if (existing is not null
                    && File.Exists(existing.LauncherPath)
                    && !PathSafety.IsSamePath(existing.LauncherPath, Environment.ProcessPath ?? ""))
                {
                    StartInstalledLauncher(existing.LauncherPath, existing.InstallPath, dataStore.Root, firstInstall: false);
                    return LauncherExitCodes.Success;
                }

                var installer = new MainWindow(bootstrap, dataStore, installDryRun);
                _ = installer.ShowDialog();
                if (installDryRun && installer.SimulationCompleted)
                {
                    var simulationWindow = new MainWindow(
                        workspace,
                        simulationMode: true,
                        simulationDisplayName: bootstrap.DisplayName);
                    _ = simulationWindow.ShowDialog();
                    return LauncherExitCodes.Success;
                }

                if (installer.DialogResult == true
                    && installer.InstalledPath is not null
                    && installer.InstalledLauncherPath is not null)
                {
                    StartInstalledLauncher(
                        installer.InstalledLauncherPath,
                        installer.InstalledPath,
                        dataStore.Root,
                        firstInstall: true);
                }

                return LauncherExitCodes.Success;
            }

            var window = new MainWindow(
                workspace,
                previewMode,
                firstInstall,
                simulationDisplayName: bootstrap?.DisplayName);
            _ = window.ShowDialog();
            return window.ExitCode;
        }
        finally
        {
            application.Shutdown();
        }
    }

    private static void StartInstalledLauncher(
        string executable,
        string workingDirectory,
        string configDirectory,
        bool firstInstall)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false
        };
        if (firstInstall)
        {
            startInfo.ArgumentList.Add("--first-install");
        }

        startInfo.ArgumentList.Add("--config-dir");
        startInfo.ArgumentList.Add(configDirectory);
        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("无法启动已安装的启动器。");
        }
    }

    private static StartupArguments ParseStartupArguments(string[] args)
    {
        var remaining = new List<string>();
        string? configDirectory = null;
        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.Equals("--config-dir", StringComparison.OrdinalIgnoreCase))
            {
                if (++index >= args.Length)
                {
                    throw new ArgumentException("--config-dir 需要一个目录参数。");
                }

                configDirectory = args[index];
            }
            else if (argument.StartsWith("--config-dir=", StringComparison.OrdinalIgnoreCase))
            {
                configDirectory = argument[(argument.IndexOf('=') + 1)..];
            }
            else
            {
                remaining.Add(argument);
            }
        }

        return new StartupArguments(remaining, configDirectory);
    }

    private sealed record StartupArguments(List<string> Arguments, string? ConfigDirectory);
}
