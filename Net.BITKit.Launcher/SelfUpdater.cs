using System.Diagnostics;
using System.Text.Json;

namespace Net.BITKit.Launcher;

public static class SelfUpdater
{
    public const string ApplyCommand = "__apply-self-update";
    public const string CleanupCommand = "__cleanup-self-update";

    public static void Schedule(
        string stagedExecutable,
        string targetExecutable,
        string stagingRoot,
        string workingDirectory,
        string launcher)
    {
        var currentExecutable = Environment.ProcessPath
                                ?? throw new InvalidOperationException("无法确定当前启动器路径。");
        if (!PathSafety.IsSamePath(currentExecutable, targetExecutable))
        {
            throw new InvalidOperationException("自更新目标不是当前启动器。");
        }

        var helperRoot = Path.Combine(
            Path.GetTempPath(),
            "Net.BITKit.Launcher",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(helperRoot);

        try
        {
            var helperPath = CopyRuntimeSet(currentExecutable, helperRoot);
            var startInfo = CreateStartInfo(helperPath, workingDirectory);
            startInfo.ArgumentList.Add(ApplyCommand);
            startInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            startInfo.ArgumentList.Add(stagedExecutable);
            startInfo.ArgumentList.Add(targetExecutable);
            startInfo.ArgumentList.Add(stagingRoot);
            startInfo.ArgumentList.Add(helperRoot);
            startInfo.ArgumentList.Add(workingDirectory);
            startInfo.ArgumentList.Add(launcher);

            if (Process.Start(startInfo) is null)
            {
                throw new InvalidOperationException("无法启动临时自更新进程。");
            }
        }
        catch
        {
            TryDeleteDirectory(helperRoot);
            throw;
        }
    }

    public static async Task<int> ApplyAsync(string[] args)
    {
        if (args.Length != 8
            || !int.TryParse(args[1], out var parentProcessId))
        {
            Console.Error.WriteLine("自更新参数无效。");
            return 2;
        }

        var stagedExecutable = Path.GetFullPath(args[2]);
        var targetExecutable = Path.GetFullPath(args[3]);
        var stagingRoot = Path.GetFullPath(args[4]);
        var helperRoot = Path.GetFullPath(args[5]);
        var workingDirectory = Path.GetFullPath(args[6]);
        var launcher = args[7];
        var backupPath = Path.Combine(stagingRoot, "launcher.rollback");

        try
        {
            await WaitForProcessExitAsync(parentProcessId, TimeSpan.FromMinutes(2));
            await RetryAsync(() => ReplaceWithBackup(stagedExecutable, targetExecutable, backupPath));

            var cleanupInfo = CreateStartInfo(targetExecutable, workingDirectory);
            cleanupInfo.ArgumentList.Add(CleanupCommand);
            cleanupInfo.ArgumentList.Add(Environment.ProcessId.ToString());
            cleanupInfo.ArgumentList.Add(helperRoot);
            cleanupInfo.ArgumentList.Add(stagingRoot);
            cleanupInfo.ArgumentList.Add(workingDirectory);
            cleanupInfo.ArgumentList.Add(launcher);
            if (Process.Start(cleanupInfo) is null)
            {
                throw new InvalidOperationException("新版启动器无法启动。");
            }

            return 0;
        }
        catch (Exception exception)
        {
            TryRestoreBackup(backupPath, targetExecutable);
            TryWriteErrorLog(helperRoot, exception);
            return 1;
        }
    }

    public static async Task<int> CleanupAsync(string[] args)
    {
        if (args.Length != 6
            || !int.TryParse(args[1], out var helperProcessId))
        {
            Console.Error.WriteLine("自更新清理参数无效。");
            return 2;
        }

        var helperRoot = Path.GetFullPath(args[2]);
        var stagingRoot = Path.GetFullPath(args[3]);
        var workingDirectory = Path.GetFullPath(args[4]);
        var launcher = args[5];

        await WaitForProcessExitAsync(helperProcessId, TimeSpan.FromMinutes(1));
        await RetryDeleteDirectoryAsync(helperRoot);
        await RetryDeleteDirectoryAsync(stagingRoot);
        TryDeleteEmptyWorkingRoot(stagingRoot);

        if (!string.IsNullOrWhiteSpace(launcher))
        {
            ProcessLauncher.Launch(workingDirectory, launcher);
        }

        return 0;
    }

    private static string CopyRuntimeSet(string executablePath, string helperRoot)
    {
        var sourceDirectory = Path.GetDirectoryName(executablePath)!;
        var executableName = Path.GetFileName(executablePath);
        var helperPath = Path.Combine(helperRoot, executableName);
        CopyFile(executablePath, helperPath);

        var baseName = Path.GetFileNameWithoutExtension(executablePath);
        var companions = new[]
        {
            $"{baseName}.dll",
            $"{baseName}.deps.json",
            $"{baseName}.runtimeconfig.json"
        };

        foreach (var companion in companions)
        {
            var source = Path.Combine(sourceDirectory, companion);
            if (File.Exists(source))
            {
                CopyFile(source, Path.Combine(helperRoot, companion));
            }
        }

        var depsPath = Path.Combine(sourceDirectory, $"{baseName}.deps.json");
        if (File.Exists(depsPath))
        {
            CopyDependencyAssets(depsPath, sourceDirectory, helperRoot);
        }

        return helperPath;
    }

    private static void CopyDependencyAssets(string depsPath, string sourceDirectory, string helperRoot)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(depsPath));
        if (!document.RootElement.TryGetProperty("targets", out var targets))
        {
            return;
        }

        foreach (var target in targets.EnumerateObject())
            foreach (var library in target.Value.EnumerateObject())
                foreach (var assetGroupName in new[] { "runtime", "native", "runtimeTargets" })
                {
                    if (!library.Value.TryGetProperty(assetGroupName, out var assetGroup))
                    {
                        continue;
                    }

                    foreach (var asset in assetGroup.EnumerateObject())
                    {
                        var fileName = Path.GetFileName(asset.Name.Replace('/', Path.DirectorySeparatorChar));
                        var source = Path.Combine(sourceDirectory, fileName);
                        if (File.Exists(source))
                        {
                            CopyFile(source, Path.Combine(helperRoot, fileName));
                        }
                    }
                }
    }

    private static ProcessStartInfo CreateStartInfo(string executable, string workingDirectory) => new()
    {
        FileName = executable,
        WorkingDirectory = workingDirectory,
        UseShellExecute = false,
        CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden
    };

    private static void ReplaceWithBackup(string source, string target, string backup)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Delete(backup);
        if (File.Exists(target))
        {
            File.Replace(source, target, backup, ignoreMetadataErrors: true);
        }
        else
        {
            File.Move(source, target);
        }
    }

    private static void TryRestoreBackup(string backup, string target)
    {
        try
        {
            if (!File.Exists(backup))
            {
                return;
            }

            File.Delete(target);
            File.Move(backup, target);
        }
        catch
        {
            // 错误日志仍保留在临时目录。
        }
    }

    private static async Task WaitForProcessExitAsync(int processId, TimeSpan timeout)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            using var cancellation = new CancellationTokenSource(timeout);
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (ArgumentException)
        {
            // 进程已经退出。
        }
    }

    private static async Task RetryAsync(Action action)
    {
        Exception? lastError = null;
        for (var attempt = 0; attempt < 50; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (IOException exception)
            {
                lastError = exception;
                await Task.Delay(200);
            }
            catch (UnauthorizedAccessException exception)
            {
                lastError = exception;
                await Task.Delay(200);
            }
        }

        throw new IOException("等待启动器文件解锁超时。", lastError);
    }

    private static async Task RetryDeleteDirectoryAsync(string path)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException)
            {
                await Task.Delay(200);
            }
            catch (UnauthorizedAccessException)
            {
                await Task.Delay(200);
            }
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // 启动失败时尽力清理。
        }
    }

    private static void TryWriteErrorLog(string helperRoot, Exception exception)
    {
        try
        {
            File.WriteAllText(Path.Combine(helperRoot, "self-update-error.log"), exception.ToString());
        }
        catch
        {
            // 无法记录时不覆盖原异常。
        }
    }

    private static void TryDeleteEmptyWorkingRoot(string stagingPath)
    {
        try
        {
            var parent = Directory.GetParent(stagingPath)?.FullName;
            if (parent is not null
                && Path.GetFileName(parent).Equals(
                    ManifestService.WorkingDirectoryName,
                    StringComparison.OrdinalIgnoreCase)
                && Directory.Exists(parent)
                && !Directory.EnumerateFileSystemEntries(parent).Any())
            {
                Directory.Delete(parent);
            }
        }
        catch
        {
            // 父工作目录为空时尽力清理。
        }
    }

    private static void CopyFile(string source, string destination) =>
        File.Copy(source, destination, overwrite: true);
}
