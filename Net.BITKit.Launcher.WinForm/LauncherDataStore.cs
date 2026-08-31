using System.Text.Json;

namespace Net.BITKit.Launcher.WinForm;

internal sealed record InstallationRecord(
    string ProductId,
    string InstallPath,
    string LauncherPath,
    string? GameDataDirectory,
    DateTimeOffset InstalledAt);

internal sealed class LauncherDataStore
{
    public const string EnvironmentVariable = "BITKIT_LAUNCHER_HOME";
    private readonly string _root;

    public LauncherDataStore(string? configuredRoot)
    {
        var root = !string.IsNullOrWhiteSpace(configuredRoot)
            ? configuredRoot
            : Environment.GetEnvironmentVariable(EnvironmentVariable);
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(root)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BITKit",
                "Launcher")
            : Environment.ExpandEnvironmentVariables(root));
    }

    public string Root => _root;

    public InstallationRecord? Load(string productId)
    {
        var path = GetRecordPath(productId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InstallationRecord>(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    public void Save(InstallationRecord record)
    {
        var path = GetRecordPath(record.ProductId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(record, new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private string GetRecordPath(string productId)
    {
        var safeProductId = string.Concat(productId.Select(character =>
            char.IsLetterOrDigit(character) || character is '.' or '-' or '_'
                ? character
                : '_'));
        return Path.Combine(_root, "installations", safeProductId + ".json");
    }
}
