using System;
using System.Collections.Generic;

namespace Net.BITKit.Launcher
{
    [Serializable]
    public sealed class Manifest
    {
        public string Name { get; set; } = "";
        public string BaseUrl { get; set; } = "";
        public string Version { get; set; } = "0.0.1";
        public string Launcher { get; set; } = "";
        public string? GameArguments { get; set; }
        public Dictionary<string, string> GameEnvironment { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public string? HashAlgorithm { get; set; }
        public Announcement? Announcement { get; set; }
        public LauncherUi? Ui { get; set; }
        public List<FileEntry> Files { get; set; } = new List<FileEntry>();
    }

    [Serializable]
    public sealed class Announcement
    {
        public string Title { get; set; } = "公告";
        public string Content { get; set; } = "";
        public string? Url { get; set; }
    }

    [Serializable]
    public sealed class LauncherUi
    {
        public string Preset { get; set; } = "gameHero";
        public string? Background { get; set; }
        public string AccentColor { get; set; } = "#F8C63D";
        public string ButtonText { get; set; } = "启动游戏";
    }

    [Serializable]
    public sealed class FileEntry
    {
        public string Path { get; set; } = "";
        public long Size { get; set; }
        public string Hash { get; set; } = "";
        public string? Url { get; set; }
        public bool Required { get; set; } = true;
    }
}
