namespace Net.BITKit.Launcher.WinForm;

internal static class LauncherExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int InvalidArguments = 2;
    public const int AlreadyRunning = 10;
    public const int FilesInUse = 11;
    public const int Cancelled = 130;
}
