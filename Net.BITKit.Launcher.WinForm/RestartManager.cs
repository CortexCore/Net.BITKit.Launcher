using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using Net.BITKit.Launcher;

namespace Net.BITKit.Launcher.WinForm;

internal sealed record BlockingProcess(int ProcessId, string Name, bool Restartable);

internal static class RestartManager
{
    private const int ErrorSuccess = 0;
    private const int ErrorMoreData = 234;
    private const int MaxApplicationName = 255;
    private const int MaxServiceName = 63;

    public static IReadOnlyList<BlockingProcess> GetBlockingProcesses(IEnumerable<string> paths)
    {
        var existingPaths = paths
            .Where(File.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (existingPaths.Length == 0)
        {
            return [];
        }

        var sessionKey = new StringBuilder(32);
        var result = RmStartSession(out var session, 0, sessionKey);
        if (result != ErrorSuccess)
        {
            throw new Win32Exception(result, "无法启动 Windows Restart Manager 会话。");
        }

        try
        {
            result = RmRegisterResources(
                session,
                (uint)existingPaths.Length,
                existingPaths,
                0,
                null,
                0,
                null);
            if (result != ErrorSuccess)
            {
                throw new Win32Exception(result, "无法向 Restart Manager 注册待更新文件。");
            }

            uint needed;
            uint count = 0;
            uint rebootReasons = 0;
            result = RmGetList(session, out needed, ref count, null, ref rebootReasons);
            if (result == ErrorSuccess && needed == 0)
            {
                return [];
            }

            if (result != ErrorMoreData)
            {
                throw new Win32Exception(result, "Restart Manager 无法读取文件占用信息。");
            }

            while (true)
            {
                var processes = new RmProcessInfo[needed];
                count = needed;
                result = RmGetList(session, out needed, ref count, processes, ref rebootReasons);
                if (result == ErrorMoreData)
                {
                    continue;
                }

                if (result != ErrorSuccess)
                {
                    throw new Win32Exception(result, "Restart Manager 无法读取文件占用信息。");
                }

                return processes
                    .Take((int)count)
                    .Where(process => process.Process.ProcessId != Environment.ProcessId)
                    .Select(process => new BlockingProcess(
                        process.Process.ProcessId,
                        ResolveProcessName(process),
                        process.Restartable))
                    .GroupBy(process => process.ProcessId)
                    .Select(group => group.First())
                    .OrderBy(process => process.Name, StringComparer.CurrentCultureIgnoreCase)
                    .ToArray();
            }
        }
        finally
        {
            RmEndSession(session);
        }
    }

    public static IReadOnlyList<string> GetChangedPaths(UpdatePlan plan) =>
        plan.Downloads
            .Where(file => !file.IsSelfUpdate)
            .Select(file => file.TargetPath)
            .Concat(plan.Deletions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string ResolveProcessName(RmProcessInfo process)
    {
        if (!string.IsNullOrWhiteSpace(process.ApplicationName))
        {
            return process.ApplicationName;
        }

        try
        {
            using var systemProcess = Process.GetProcessById(process.Process.ProcessId);
            return systemProcess.ProcessName;
        }
        catch
        {
            return $"PID {process.Process.ProcessId}";
        }
    }

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint sessionHandle, int sessionFlags, StringBuilder sessionKey);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(
        uint sessionHandle,
        uint fileCount,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] string[]? fileNames,
        uint applicationCount,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] RmUniqueProcess[]? applications,
        uint serviceCount,
        [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] string[]? serviceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(
        uint sessionHandle,
        out uint processInfoNeeded,
        ref uint processInfoCount,
        [In, Out] RmProcessInfo[]? affectedApplications,
        ref uint rebootReasons);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint sessionHandle);

    [StructLayout(LayoutKind.Sequential)]
    private struct RmUniqueProcess
    {
        public int ProcessId;
        public FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RmProcessInfo
    {
        public RmUniqueProcess Process;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxApplicationName + 1)]
        public string ApplicationName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxServiceName + 1)]
        public string ServiceShortName;

        public uint ApplicationType;
        public uint ApplicationStatus;
        public uint TerminalServicesSessionId;

        [MarshalAs(UnmanagedType.Bool)]
        public bool Restartable;
    }
}
