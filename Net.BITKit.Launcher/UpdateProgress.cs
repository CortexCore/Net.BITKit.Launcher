namespace Net.BITKit.Launcher;

public enum UpdateStage
{
    Checking,
    Ready,
    Downloading,
    Applying,
    Completed
}

public sealed record UpdateProgress(
    UpdateStage Stage,
    string Message,
    string? CurrentFile = null,
    long BytesCompleted = 0,
    long BytesTotal = 0,
    int FilesCompleted = 0,
    int FilesTotal = 0);
