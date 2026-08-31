namespace Net.BITKit.Launcher;

public static class PathSafety
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    public static string ResolveUnderRoot(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidDataException("清单包含空文件路径。");
        }

        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            throw new InvalidDataException($"清单路径不能是绝对路径: {relativePath}");
        }

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"清单路径包含非法目录段: {relativePath}");
        }

        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, normalized));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(rootPrefix, PathComparison))
        {
            throw new InvalidDataException($"清单路径越出工作目录: {relativePath}");
        }

        EnsureNoReparsePointTraversal(fullRoot, candidate);
        return candidate;
    }

    public static bool IsSamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), PathComparison);

    public static bool IsUnderRoot(string root, string path)
    {
        var fullRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(path);
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        return candidate.StartsWith(rootPrefix, PathComparison);
    }

    private static void EnsureNoReparsePointTraversal(string root, string candidate)
    {
        var relative = Path.GetRelativePath(root, candidate);
        var current = root;
        foreach (var segment in relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if ((!Directory.Exists(current) && !File.Exists(current))
                || (File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0)
            {
                continue;
            }

            throw new InvalidDataException($"更新路径不能经过符号链接或目录联接: {relative}");
        }
    }
}
