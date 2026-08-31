using System.Runtime.InteropServices;

namespace Net.BITKit.Launcher.WinForm;

internal static class ConsoleWindow
{
    private const int SwHide = 0;

    public static void HideIfOwnedByCurrentProcess()
    {
        var window = GetConsoleWindow();
        if (window == IntPtr.Zero)
        {
            return;
        }

        var processes = new uint[8];
        var processCount = GetConsoleProcessList(processes, (uint)processes.Length);
        if (processCount == 1 && processes[0] == (uint)Environment.ProcessId)
        {
            _ = ShowWindow(window, SwHide);
        }
    }

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("kernel32.dll")]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);
}
