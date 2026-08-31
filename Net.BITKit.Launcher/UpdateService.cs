using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace Net.BITKit.Launcher;

public sealed record PlannedFile(FileEntry Entry, string TargetPath, bool IsSelfUpdate);

public sealed record UpdatePlan(
    Manifest LocalManifest,
    Manifest RemoteManifest,
    Uri BaseUri,
    IReadOnlyList<PlannedFile> Downloads,
    IReadOnlyList<string> Deletions,
    int SkippedCount)
{
    public bool RequiresSelfUpdate => Downloads.Any(file => file.IsSelfUpdate);
}

public sealed record UpdateResult(UpdatePlan Plan, bool SelfUpdateScheduled, int DownloadedCount);

public sealed class UpdateService
{
    private readonly ManifestService _manifestService;
    private readonly HttpClient _httpClient;
    private readonly Action<string> _log;
    private readonly IProgress<UpdateProgress>? _progress;

    public UpdateService(
        ManifestService manifestService,
        HttpClient httpClient,
        Action<string>? log = null,
        IProgress<UpdateProgress>? progress = null)
    {
        _manifestService = manifestService;
        _httpClient = httpClient;
        _log = log ?? (_ => { });
        _progress = progress;
    }

    public async Task<UpdatePlan> BuildPlanAsync(
        string root,
        CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        Report(new UpdateProgress(UpdateStage.Checking, "正在检查更新…"));
        var localManifestPath = Path.Combine(root, ManifestService.FileName);
        if (!File.Exists(localManifestPath))
        {
            throw new FileNotFoundException("本地 manifest.yaml 不存在，请先生成并配置 BaseUrl。", localManifestPath);
        }

        var localManifest = await _manifestService.LoadAsync(localManifestPath, cancellationToken);
        var baseUri = ValidateBaseUri(localManifest.BaseUrl);
        var manifestUri = new Uri(EnsureTrailingSlash(baseUri), ManifestService.FileName);
        _log($"获取远端清单: {manifestUri}");

        using var response = await _httpClient.GetAsync(manifestUri, cancellationToken);
        response.EnsureSuccessStatusCode();
        var yaml = await response.Content.ReadAsStringAsync(cancellationToken);
        var remoteManifest = _manifestService.Deserialize(yaml);
        remoteManifest.BaseUrl = localManifest.BaseUrl;

        ValidateManifest(remoteManifest, root);
        remoteManifest.Files = remoteManifest.Files
            .Where(entry => !ManifestService.NormalizeRelativePath(entry.Path)
                .Equals(ManifestService.FileName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var currentExecutable = Environment.ProcessPath;
        var downloads = new List<PlannedFile>();
        var skipped = 0;
        foreach (var entry in remoteManifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var targetPath = PathSafety.ResolveUnderRoot(root, entry.Path);
            var isSelfUpdate = currentExecutable is not null
                               && PathSafety.IsUnderRoot(root, currentExecutable)
                               && PathSafety.IsSamePath(currentExecutable, targetPath);

            if (File.Exists(targetPath)
                && await HasExpectedHashAsync(targetPath, remoteManifest.HashAlgorithm, entry.Hash, cancellationToken))
            {
                skipped++;
                continue;
            }

            downloads.Add(new PlannedFile(entry, targetPath, isSelfUpdate));
        }

        var deletions = await FindSafeDeletionsAsync(
            root,
            localManifest,
            remoteManifest,
            currentExecutable,
            cancellationToken);

        var plan = new UpdatePlan(localManifest, remoteManifest, baseUri, downloads, deletions, skipped);
        Report(new UpdateProgress(
            UpdateStage.Ready,
            downloads.Count == 0 && deletions.Count == 0
                ? "当前已经是最新版本。"
                : $"发现 {downloads.Count + deletions.Count} 项变更。",
            FilesCompleted: skipped,
            FilesTotal: remoteManifest.Files.Count));
        return plan;
    }

    public async Task<UpdateResult> ApplyAsync(
        string root,
        UpdatePlan plan,
        int maxConcurrency,
        bool launchAfterUpdate,
        CancellationToken cancellationToken = default)
    {
        root = Path.GetFullPath(root);
        maxConcurrency = Math.Clamp(maxConcurrency, 1, 32);
        var stagingRoot = Path.Combine(root, ManifestService.WorkingDirectoryName, Guid.NewGuid().ToString("N"));
        var downloadRoot = Path.Combine(stagingRoot, "downloads");
        var backupRoot = Path.Combine(stagingRoot, "backups");
        Directory.CreateDirectory(downloadRoot);

        try
        {
            var downloaded = await DownloadAllAsync(
                plan,
                downloadRoot,
                maxConcurrency,
                cancellationToken);

            var selfUpdate = downloaded.SingleOrDefault(file => file.Planned.IsSelfUpdate);
            var manifestStagePath = Path.Combine(stagingRoot, "manifest.next.yaml");
            await File.WriteAllTextAsync(
                manifestStagePath,
                _manifestService.Serialize(plan.RemoteManifest),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);

            var transaction = new FileTransaction(backupRoot);
            try
            {
                Report(new UpdateProgress(
                    UpdateStage.Applying,
                    "正在应用更新…",
                    FilesCompleted: downloaded.Count,
                    FilesTotal: downloaded.Count));
                foreach (var file in downloaded.Where(file => !file.Planned.IsSelfUpdate))
                {
                    transaction.Replace(file.StagedPath, file.Planned.TargetPath);
                }

                foreach (var deletion in plan.Deletions)
                {
                    transaction.Remove(deletion);
                }

                transaction.Replace(manifestStagePath, Path.Combine(root, ManifestService.FileName));

                var selfUpdateScheduled = false;
                if (selfUpdate is not null)
                {
                    SelfUpdater.Schedule(
                        selfUpdate.StagedPath,
                        selfUpdate.Planned.TargetPath,
                        stagingRoot,
                        root,
                        launchAfterUpdate ? plan.RemoteManifest.Launcher : "");
                    selfUpdateScheduled = true;
                }

                transaction.Commit();
                if (!selfUpdateScheduled)
                {
                    Directory.Delete(stagingRoot, recursive: true);
                    TryDeleteEmptyWorkingRoot(stagingRoot);
                }

                Report(new UpdateProgress(
                    UpdateStage.Completed,
                    selfUpdateScheduled ? "正在完成启动器自更新…" : "更新完成。",
                    FilesCompleted: downloaded.Count,
                    FilesTotal: downloaded.Count));
                return new UpdateResult(plan, selfUpdateScheduled, downloaded.Count);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch
        {
            TryDeleteDirectory(stagingRoot);
            throw;
        }
    }

    private async Task<List<DownloadedFile>> DownloadAllAsync(
        UpdatePlan plan,
        string downloadRoot,
        int maxConcurrency,
        CancellationToken cancellationToken)
    {
        var downloaded = new ConcurrentBag<DownloadedFile>();
        var bytesTotal = plan.Downloads.Sum(file => Math.Max(0, file.Entry.Size));
        long bytesCompleted = 0;
        var filesCompleted = 0;
        Report(new UpdateProgress(
            UpdateStage.Downloading,
            "正在下载更新…",
            BytesTotal: bytesTotal,
            FilesTotal: plan.Downloads.Count));
        await Parallel.ForEachAsync(
            plan.Downloads,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = maxConcurrency,
                CancellationToken = cancellationToken
            },
            async (planned, token) =>
            {
                try
                {
                    var stagedPath = PathSafety.ResolveUnderRoot(downloadRoot, planned.Entry.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(stagedPath)!);
                    await DownloadOneAsync(
                        plan,
                        planned.Entry,
                        stagedPath,
                        bytesRead =>
                        {
                            var total = Interlocked.Add(ref bytesCompleted, bytesRead);
                            Report(new UpdateProgress(
                                UpdateStage.Downloading,
                                "正在下载更新…",
                                planned.Entry.Path,
                                total,
                                bytesTotal,
                                Volatile.Read(ref filesCompleted),
                                plan.Downloads.Count));
                        },
                        token);
                    downloaded.Add(new DownloadedFile(planned, stagedPath));
                    var completed = Interlocked.Increment(ref filesCompleted);
                    Report(new UpdateProgress(
                        UpdateStage.Downloading,
                        $"已完成 {completed}/{plan.Downloads.Count}",
                        planned.Entry.Path,
                        Volatile.Read(ref bytesCompleted),
                        bytesTotal,
                        completed,
                        plan.Downloads.Count));
                    _log($"已下载并校验: {planned.Entry.Path}");
                }
                catch (Exception exception) when (!planned.Entry.Required)
                {
                    _log($"可选文件下载失败，已跳过 {planned.Entry.Path}: {exception.Message}");
                }
            });

        return downloaded.ToList();
    }

    private async Task DownloadOneAsync(
        UpdatePlan plan,
        FileEntry entry,
        string stagedPath,
        Action<int> onBytesRead,
        CancellationToken cancellationToken)
    {
        var uri = ResolveDownloadUri(plan.BaseUri, entry);
        using var response = await _httpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using (var input = await response.Content.ReadAsStreamAsync(cancellationToken))
        await using (var output = new FileStream(
                         stagedPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         81920,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            var buffer = new byte[81920];
            int bytesRead;
            while ((bytesRead = await input.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                onBytesRead(bytesRead);
            }
        }

        var actualSize = new FileInfo(stagedPath).Length;
        if (actualSize != entry.Size)
        {
            throw new InvalidDataException(
                $"文件大小不匹配 {entry.Path}: 期望 {entry.Size}，实际 {actualSize}。");
        }

        if (!await HasExpectedHashAsync(
                stagedPath,
                plan.RemoteManifest.HashAlgorithm,
                entry.Hash,
                cancellationToken))
        {
            throw new InvalidDataException($"下载文件哈希不匹配: {entry.Path}");
        }
    }

    private static async Task<bool> HasExpectedHashAsync(
        string path,
        string? declaredAlgorithm,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        var algorithm = HashService.ResolveAlgorithm(declaredAlgorithm, expectedHash);
        var actual = await HashService.CalculateFileAsync(path, algorithm, cancellationToken);
        return actual.Equals(expectedHash.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<List<string>> FindSafeDeletionsAsync(
        string root,
        Manifest local,
        Manifest remote,
        string? currentExecutable,
        CancellationToken cancellationToken)
    {
        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var remotePaths = remote.Files
            .Select(entry => ManifestService.NormalizeRelativePath(entry.Path))
            .ToHashSet(comparer);
        var deletions = new List<string>();

        foreach (var entry in local.Files)
        {
            var relativePath = ManifestService.NormalizeRelativePath(entry.Path);
            if (relativePath.Equals(ManifestService.FileName, StringComparison.OrdinalIgnoreCase)
                || remotePaths.Contains(relativePath))
            {
                continue;
            }

            string targetPath;
            try
            {
                targetPath = PathSafety.ResolveUnderRoot(root, relativePath);
            }
            catch (InvalidDataException)
            {
                continue;
            }

            if (!File.Exists(targetPath)
                || currentExecutable is not null && PathSafety.IsSamePath(currentExecutable, targetPath)
                || string.IsNullOrWhiteSpace(entry.Hash))
            {
                continue;
            }

            if (await HasExpectedHashAsync(targetPath, local.HashAlgorithm, entry.Hash, cancellationToken))
            {
                deletions.Add(targetPath);
            }
        }

        return deletions;
    }

    private static void ValidateManifest(Manifest manifest, string root)
    {
        if (manifest.Files.Count == 0)
        {
            throw new InvalidDataException("远端清单没有文件信息。");
        }

        var comparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var paths = new HashSet<string>(comparer);
        foreach (var entry in manifest.Files)
        {
            var normalized = ManifestService.NormalizeRelativePath(entry.Path);
            PathSafety.ResolveUnderRoot(root, normalized);
            if (!paths.Add(normalized))
            {
                throw new InvalidDataException($"远端清单包含重复路径: {entry.Path}");
            }

            if (entry.Size < 0 || string.IsNullOrWhiteSpace(entry.Hash))
            {
                throw new InvalidDataException($"远端清单文件信息无效: {entry.Path}");
            }

            HashService.ResolveAlgorithm(manifest.HashAlgorithm, entry.Hash);
        }

        if (!string.IsNullOrWhiteSpace(manifest.Launcher))
        {
            PathSafety.ResolveUnderRoot(root, manifest.Launcher);
        }
    }

    private static Uri ValidateBaseUri(string baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException("BaseUrl 必须是绝对 HTTP 或 HTTPS 地址。");
        }

        return uri;
    }

    private static Uri ResolveDownloadUri(Uri baseUri, FileEntry entry)
    {
        Uri uri;
        if (!string.IsNullOrWhiteSpace(entry.Url))
        {
            uri = Uri.TryCreate(entry.Url, UriKind.Absolute, out var absolute)
                ? absolute
                : new Uri(EnsureTrailingSlash(baseUri), entry.Url);
        }
        else
        {
            var escapedPath = string.Join(
                '/',
                ManifestService.NormalizeRelativePath(entry.Path)
                    .Split('/', StringSplitOptions.RemoveEmptyEntries)
                    .Select(Uri.EscapeDataString));
            uri = new Uri(EnsureTrailingSlash(baseUri), escapedPath);
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            throw new InvalidDataException($"文件下载地址必须使用 HTTP 或 HTTPS: {entry.Path}");
        }

        return uri;
    }

    private static Uri EnsureTrailingSlash(Uri uri) =>
        uri.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? uri
            : new Uri(uri.AbsoluteUri + '/');

    private void Report(UpdateProgress progress) => _progress?.Report(progress);

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
                TryDeleteEmptyWorkingRoot(path);
            }
        }
        catch
        {
            // 保留现场，便于定位更新失败原因。
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

    private sealed record DownloadedFile(PlannedFile Planned, string StagedPath);
}
