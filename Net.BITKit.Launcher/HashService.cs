using System.Security.Cryptography;

namespace Net.BITKit.Launcher;

public static class HashService
{
    public const string Md5 = "MD5";
    public const string Sha256 = "SHA256";

    public static async Task<string> CalculateFileAsync(
        string path,
        string? algorithm,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await CalculateAsync(stream, algorithm, cancellationToken);
    }

    public static async Task<string> CalculateAsync(
        Stream stream,
        string? algorithm,
        CancellationToken cancellationToken = default)
    {
        using var hash = CreateAlgorithm(NormalizeAlgorithm(algorithm));
        return Convert.ToHexString(await hash.ComputeHashAsync(stream, cancellationToken));
    }

    public static string ResolveAlgorithm(string? declaredAlgorithm, string expectedHash)
    {
        if (!string.IsNullOrWhiteSpace(declaredAlgorithm))
        {
            return NormalizeAlgorithm(declaredAlgorithm);
        }

        return expectedHash.Trim().Length switch
        {
            32 => Md5,
            64 => Sha256,
            _ => throw new InvalidDataException("无法根据哈希长度判断算法。")
        };
    }

    public static HashAlgorithm CreateAlgorithm(string algorithm) => NormalizeAlgorithm(algorithm) switch
    {
        Md5 => MD5.Create(),
        Sha256 => SHA256.Create(),
        _ => throw new InvalidDataException($"不支持的哈希算法: {algorithm}")
    };

    private static string NormalizeAlgorithm(string? algorithm)
    {
        var normalized = (algorithm ?? Sha256)
            .Replace("-", "", StringComparison.Ordinal)
            .Trim()
            .ToUpperInvariant();
        return normalized switch
        {
            Md5 => Md5,
            Sha256 => Sha256,
            _ => throw new InvalidDataException($"不支持的哈希算法: {algorithm}")
        };
    }
}
