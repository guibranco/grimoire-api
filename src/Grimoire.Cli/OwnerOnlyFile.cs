namespace Grimoire.Cli;

/// <summary>Files that hold secrets: readable and writable only by their owner on Unix.</summary>
internal static class OwnerOnlyFile
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    /// <summary>Creates or truncates <paramref name="path"/>, owner-only before anything is written.</summary>
    public static FileStream Create(string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.Create,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = OwnerReadWrite;
        var stream = new FileStream(path, options);
        if (!OperatingSystem.IsWindows())
            // UnixCreateMode only applies to new files; an existing file keeps its old mode.
            File.SetUnixFileMode(path, OwnerReadWrite);
        return stream;
    }
}
