namespace Grimoire.Infrastructure.Backups;

public sealed class LocalDirectoryBackupTarget(string directory) : IBackupTarget
{
    private readonly string _directory = Path.GetFullPath(directory);

    public string Name => "local";

    public async Task WriteAsync(string name, byte[] content, CancellationToken ct)
    {
        EnsureDirectory();
        var path = PathFor(name);
        var temp = path + ".tmp";
        await File.WriteAllBytesAsync(temp, content, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        // Atomic rename: a crash mid-write never leaves a truncated file with a backup name.
        File.Move(temp, path, overwrite: true);
    }

    public Task<IReadOnlyList<BackupObject>> ListAsync(CancellationToken ct)
    {
        if (!Directory.Exists(_directory))
            return Task.FromResult<IReadOnlyList<BackupObject>>([]);

        IReadOnlyList<BackupObject> result = new DirectoryInfo(_directory)
            .EnumerateFiles("*" + BackupNaming.Extension)
            .Where(f => BackupNaming.IsBackupName(f.Name))
            .Select(f => new BackupObject(f.Name, f.Length))
            .ToList();
        return Task.FromResult(result);
    }

    public Task<byte[]> ReadAsync(string name, CancellationToken ct) =>
        File.ReadAllBytesAsync(PathFor(name), ct);

    public Task DeleteAsync(string name, CancellationToken ct)
    {
        File.Delete(PathFor(name));
        return Task.CompletedTask;
    }

    private string PathFor(string name)
    {
        if (!BackupNaming.IsBackupName(name))
            throw new ArgumentException($"'{name}' is not a backup name.", nameof(name));
        return Path.Combine(_directory, name);
    }

    private void EnsureDirectory()
    {
        if (Directory.Exists(_directory))
            return;

        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(_directory);
        else
            Directory.CreateDirectory(_directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }
}
