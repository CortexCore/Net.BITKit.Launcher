namespace Net.BITKit.Launcher;

internal sealed class FileTransaction
{
    private readonly string _backupRoot;
    private readonly List<AppliedChange> _changes = [];
    private bool _committed;

    public FileTransaction(string backupRoot)
    {
        _backupRoot = backupRoot;
        Directory.CreateDirectory(_backupRoot);
    }

    public void Replace(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        var backupPath = Path.Combine(_backupRoot, $"{_changes.Count:D6}.bak");

        if (File.Exists(targetPath))
        {
            File.Replace(sourcePath, targetPath, backupPath, ignoreMetadataErrors: true);
            _changes.Add(new AppliedChange(targetPath, backupPath, WasCreated: false));
        }
        else
        {
            File.Move(sourcePath, targetPath);
            _changes.Add(new AppliedChange(targetPath, BackupPath: null, WasCreated: true));
        }
    }

    public void Remove(string targetPath)
    {
        if (!File.Exists(targetPath))
        {
            return;
        }

        var backupPath = Path.Combine(_backupRoot, $"{_changes.Count:D6}.bak");
        File.Move(targetPath, backupPath);
        _changes.Add(new AppliedChange(targetPath, backupPath, WasCreated: false));
    }

    public void Commit() => _committed = true;

    public void Rollback()
    {
        if (_committed)
        {
            return;
        }

        List<Exception>? errors = null;
        foreach (var change in _changes.AsEnumerable().Reverse())
        {
            try
            {
                if (File.Exists(change.TargetPath))
                {
                    File.Delete(change.TargetPath);
                }

                if (!change.WasCreated && change.BackupPath is not null && File.Exists(change.BackupPath))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(change.TargetPath)!);
                    File.Move(change.BackupPath, change.TargetPath);
                }
            }
            catch (Exception exception)
            {
                (errors ??= []).Add(exception);
            }
        }

        if (errors is not null)
        {
            throw new AggregateException("更新失败，且部分文件无法回滚。", errors);
        }
    }

    private sealed record AppliedChange(string TargetPath, string? BackupPath, bool WasCreated);
}
