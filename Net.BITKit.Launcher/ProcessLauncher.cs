using System.Diagnostics;

namespace Net.BITKit.Launcher;

public static class ProcessLauncher
{
    public static void Launch(
        string root,
        string relativePath,
        string? arguments = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var path = PathSafety.ResolveUnderRoot(root, relativePath);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("启动文件不存在。", path);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = path,
            WorkingDirectory = root,
            UseShellExecute = true
        };
        if (!string.IsNullOrWhiteSpace(arguments))
        {
            startInfo.Arguments = arguments;
        }

        if (environment is not null)
        {
            foreach (var pair in environment)
            {
                if (!string.IsNullOrWhiteSpace(pair.Key))
                {
                    startInfo.Environment[pair.Key] = Environment.ExpandEnvironmentVariables(pair.Value ?? "");
                }
            }
        }
        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException($"无法启动应用: {relativePath}");
        }
    }
}
