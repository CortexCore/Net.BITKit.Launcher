using System.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Net.BITKit.Launcher;

public sealed class ManifestService
{
    public const string FileName = "manifest.yaml";
    public const string WorkingDirectoryName = ".bitkit-update";

    private readonly IDeserializer _deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private readonly ISerializer _serializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .Build();

    public Manifest Deserialize(string yaml)
    {
        try
        {
            return _deserializer.Deserialize<Manifest>(yaml)
                   ?? throw new InvalidDataException("清单内容为空。");
        }
        catch (Exception exception) when (exception is not InvalidDataException)
        {
            throw new InvalidDataException("无法解析 manifest.yaml。", exception);
        }
    }

    public async Task<Manifest> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        Deserialize(await File.ReadAllTextAsync(path, cancellationToken));

    public string Serialize(Manifest manifest) => _serializer.Serialize(manifest);

    public async Task WriteAtomicAsync(
        string path,
        Manifest manifest,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(directory, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllTextAsync(
                temporaryPath,
                Serialize(manifest),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public async Task<Manifest> GenerateAsync(
        string root,
        Action<string>? onFile = null,
        CancellationToken cancellationToken = default)
    {
        var fullRoot = Path.GetFullPath(root);
        var outputPath = Path.Combine(fullRoot, FileName);
        var existing = File.Exists(outputPath)
            ? await TryLoadExistingAsync(outputPath, cancellationToken)
            : null;

        var ignore = new Ignore.Ignore();
        var gitIgnorePath = Path.Combine(fullRoot, ".gitignore");
        if (File.Exists(gitIgnorePath))
        {
            ignore.Add(await File.ReadAllLinesAsync(gitIgnorePath, cancellationToken));
        }

        var oldEntries = existing?.Files
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Path))
            .GroupBy(entry => NormalizeRelativePath(entry.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, FileEntry>(StringComparer.OrdinalIgnoreCase);

        var manifest = new Manifest
        {
            Version = DateTime.UtcNow.ToString("yyyy.MM.dd.HHmmss"),
            Name = string.IsNullOrWhiteSpace(existing?.Name)
                ? new DirectoryInfo(fullRoot).Name
                : existing.Name,
            BaseUrl = existing?.BaseUrl ?? "",
            Launcher = existing?.Launcher ?? "",
            Announcement = existing?.Announcement,
            Ui = existing?.Ui,
            HashAlgorithm = HashService.Sha256
        };

        var files = Directory.EnumerateFiles(fullRoot, "*", SearchOption.AllDirectories)
            .Select(path => new
            {
                FullPath = path,
                RelativePath = NormalizeRelativePath(Path.GetRelativePath(fullRoot, path))
            })
            .Where(file => !ShouldExclude(file.RelativePath, ignore))
            .OrderBy(file => file.RelativePath, StringComparer.Ordinal)
            .ToArray();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            onFile?.Invoke(file.RelativePath);
            oldEntries.TryGetValue(file.RelativePath, out var oldEntry);
            manifest.Files.Add(new FileEntry
            {
                Path = file.RelativePath,
                Size = new FileInfo(file.FullPath).Length,
                Hash = await HashService.CalculateFileAsync(
                    file.FullPath,
                    manifest.HashAlgorithm,
                    cancellationToken),
                Url = oldEntry?.Url,
                Required = oldEntry?.Required ?? true
            });
        }

        await WriteAtomicAsync(outputPath, manifest, cancellationToken);
        return manifest;
    }

    private async Task<Manifest?> TryLoadExistingAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await LoadAsync(path, cancellationToken);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"警告：无法继承现有清单配置：{exception.Message}");
            return null;
        }
    }

    private static bool ShouldExclude(string relativePath, Ignore.Ignore ignore)
    {
        if (relativePath.Equals(FileName, StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(".bitkit-install.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals(WorkingDirectoryName, StringComparison.OrdinalIgnoreCase)
            || relativePath.StartsWith(WorkingDirectoryName + "/", StringComparison.OrdinalIgnoreCase)
            || relativePath.EndsWith(".download", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return ignore.IsIgnored(relativePath);
    }

    public static string NormalizeRelativePath(string path) => path.Replace('\\', '/');
}
