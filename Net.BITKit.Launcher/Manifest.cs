namespace Net.BITKit.Launcher;

// ===== 数据结构 =====

public class Manifest
{
    public string Name { get; set; }
    public string BaseUrl { get; set; }
    public string Version { get; set; } = "0.0.1";
    public string Launcher { get; set; }
    // 可选：整体校验（防篡改）
    public string? Hash { get; set; }
    public List<FileEntry> Files { get; set; } = [];
}

public class FileEntry
{
    // 相对路径（必须）
    public string Path { get; set; } = "";

    // 文件大小（快速过滤）
    public long Size { get; set; }

    // 文件哈希（核心）
    public string Hash { get; set; } = "";

    // 可选：单独下载地址（CDN分流用）
    public string? Url { get; set; }

    // 可选：是否必须（关键文件）
    public bool Required { get; set; } = true;
}