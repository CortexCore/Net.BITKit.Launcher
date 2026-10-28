namespace Net.BITKit.Launcher;

public enum ManifestFileChangeKind
{
    Added,
    Updated,
    Missing,
    Forced
}

public sealed record ManifestFileChange(FileEntry Entry, ManifestFileChangeKind Kind);

public sealed record ManifestDiff(
    Manifest Current,
    Manifest? Previous,
    IReadOnlyList<ManifestFileChange> Uploads,
    IReadOnlyList<string> Removals,
    int SkippedCount)
{
    public long TotalBytes => Current.Files.Sum(file => file.Size);
    public long UploadBytes => Uploads.Sum(change => change.Entry.Size);
}

public static class ManifestDiffService
{
    public static ManifestDiff Compare(
        Manifest current,
        Manifest? previous = null,
        string? previousRoot = null,
        bool forceAll = false)
    {
        var previousEntries = ToEntryMap(previous?.Files ?? []);
        var currentEntries = ToEntryMap(current.Files);
        var hashAlgorithmChanged = previous is not null
                                   && !string.Equals(
                                       current.HashAlgorithm,
                                       previous.HashAlgorithm,
                                       StringComparison.OrdinalIgnoreCase);
        var fullPreviousRoot = string.IsNullOrWhiteSpace(previousRoot)
            ? null
            : Path.GetFullPath(previousRoot);
        var uploads = new List<ManifestFileChange>();

        foreach (var entry in current.Files)
        {
            var path = ManifestService.NormalizeRelativePath(entry.Path);
            if (!previousEntries.TryGetValue(path, out var previousEntry))
            {
                uploads.Add(new ManifestFileChange(entry, ManifestFileChangeKind.Added));
                continue;
            }

            if (forceAll)
            {
                uploads.Add(new ManifestFileChange(entry, ManifestFileChangeKind.Forced));
                continue;
            }

            if (hashAlgorithmChanged
                || entry.Size != previousEntry.Size
                || !string.Equals(entry.Hash, previousEntry.Hash, StringComparison.OrdinalIgnoreCase))
            {
                uploads.Add(new ManifestFileChange(entry, ManifestFileChangeKind.Updated));
                continue;
            }

            if (fullPreviousRoot is null)
            {
                continue;
            }

            var deployedPath = PathSafety.ResolveUnderRoot(fullPreviousRoot, path);
            if (!File.Exists(deployedPath) || new FileInfo(deployedPath).Length != entry.Size)
            {
                uploads.Add(new ManifestFileChange(entry, ManifestFileChangeKind.Missing));
            }
        }

        var removals = previousEntries.Keys
            .Where(path => !currentEntries.ContainsKey(path))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        return new ManifestDiff(
            current,
            previous,
            uploads,
            removals,
            current.Files.Count - uploads.Count);
    }

    public static IReadOnlyList<string> CreatePreview(ManifestDiff diff)
    {
        var root = new PreviewTreeNode();
        foreach (var upload in diff.Uploads)
        {
            var node = root;
            var segments = ManifestService.NormalizeRelativePath(upload.Entry.Path).Split('/');
            for (var index = 0; index < segments.Length; index++)
            {
                var segment = segments[index];
                if (!node.Children.TryGetValue(segment, out var child))
                {
                    child = new PreviewTreeNode();
                    node.Children.Add(segment, child);
                }

                node = child;
                node.IsFile = index == segments.Length - 1;
            }
        }

        var lines = new List<string> { "Root" };
        if (root.Children.Count == 0)
        {
            lines.Add("- (empty)");
        }
        else
        {
            AppendTree(lines, root, "");
        }

        lines.Add("");
        lines.Add($"Size: {FormatBytes(diff.UploadBytes)} Files: {diff.Uploads.Count}");
        lines.Add($"Removed: {diff.Removals.Count} Skipped: {diff.SkippedCount}");
        return lines;
    }

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024d * 1024 * 1024):N2} GiB";
        }

        if (bytes >= 1024L * 1024)
        {
            return $"{bytes / (1024d * 1024):N2} MiB";
        }

        if (bytes >= 1024L)
        {
            return $"{bytes / 1024d:N2} KiB";
        }

        return $"{bytes} B";
    }

    private static void AppendTree(List<string> lines, PreviewTreeNode parent, string prefix)
    {
        var children = parent.Children
            .OrderBy(child => child.Value.IsFile)
            .ThenBy(child => child.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var child in children)
        {
            lines.Add($"{prefix}- {child.Key}{(child.Value.IsFile ? "" : "/")}");
            if (!child.Value.IsFile)
            {
                AppendTree(lines, child.Value, prefix + "  ");
            }
        }
    }

    private static Dictionary<string, FileEntry> ToEntryMap(IEnumerable<FileEntry> entries) =>
        entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(
                entry => ManifestService.NormalizeRelativePath(entry.Path),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.First(),
                StringComparer.OrdinalIgnoreCase);

    private sealed class PreviewTreeNode
    {
        public SortedDictionary<string, PreviewTreeNode> Children { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public bool IsFile { get; set; }
    }
}
