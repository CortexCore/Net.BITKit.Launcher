using System.Reflection;

namespace Net.BITKit.Launcher.WinForm;

internal sealed record BootstrapConfiguration(
    string ProductId,
    string DisplayName,
    Uri ManifestUri,
    string InstallDirectoryName,
    string? GameDataDirectory)
{
    public bool IsEnabled => !string.IsNullOrWhiteSpace(ProductId) && ManifestUri.IsAbsoluteUri;

    public static BootstrapConfiguration? Load()
    {
        var values = Assembly.GetEntryAssembly()?
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Key.StartsWith("BITKit.", StringComparison.Ordinal))
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value ?? "", StringComparer.Ordinal)
            ?? new Dictionary<string, string>();

        values.TryGetValue("BITKit.ProductId", out var productId);
        values.TryGetValue("BITKit.DisplayName", out var displayName);
        values.TryGetValue("BITKit.ManifestUrl", out var manifestUrl);
        values.TryGetValue("BITKit.InstallDirectory", out var installDirectory);
        values.TryGetValue("BITKit.GameDataDirectory", out var gameDataDirectory);
        if (string.IsNullOrWhiteSpace(productId)
            || !Uri.TryCreate(manifestUrl, UriKind.Absolute, out var manifestUri)
            || manifestUri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return new BootstrapConfiguration(
            productId,
            string.IsNullOrWhiteSpace(displayName) ? productId : displayName,
            manifestUri,
            string.IsNullOrWhiteSpace(installDirectory) ? productId : installDirectory,
            string.IsNullOrWhiteSpace(gameDataDirectory)
                ? null
                : Environment.ExpandEnvironmentVariables(gameDataDirectory));
    }
}
