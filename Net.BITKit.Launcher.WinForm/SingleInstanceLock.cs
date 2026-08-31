using System.Security.Cryptography;
using System.Text;

namespace Net.BITKit.Launcher.WinForm;

internal sealed class SingleInstanceLock : IDisposable
{
    private readonly FileStream _stream;
    private readonly string _path;
    private bool _disposed;

    private SingleInstanceLock(FileStream stream, string path)
    {
        _stream = stream;
        _path = path;
    }

    public static SingleInstanceLock? TryAcquire(string workspace)
    {
        var normalizedWorkspace = Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspace));
        if (OperatingSystem.IsWindows())
        {
            normalizedWorkspace = normalizedWorkspace.ToUpperInvariant();
        }

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedWorkspace)));
        var lockDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Net.BITKit.Launcher",
            "locks");
        Directory.CreateDirectory(lockDirectory);
        var path = Path.Combine(lockDirectory, $"{key}.lock");

        try
        {
            var stream = new FileStream(
                path,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough);
            stream.SetLength(0);
            var details = Encoding.UTF8.GetBytes(
                $"pid={Environment.ProcessId}\nworkspace={normalizedWorkspace}\nstarted={DateTimeOffset.UtcNow:O}\n");
            stream.Write(details);
            stream.Flush(flushToDisk: true);
            return new SingleInstanceLock(stream, path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stream.Dispose();
        try
        {
            File.Delete(_path);
        }
        catch
        {
            // 锁文件本身不影响下次获取，独占句柄才代表锁的所有权。
        }
    }
}
