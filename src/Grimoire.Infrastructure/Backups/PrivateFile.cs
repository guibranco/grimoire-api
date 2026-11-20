namespace Grimoire.Infrastructure.Backups;

internal static class PrivateFile
{
    /// <summary>
    /// Writes a new file that is owner-only (0600) from the moment it exists, regardless of umask,
    /// so other local users never get a window to open it. Fails if the file already exists.
    /// </summary>
    public static async Task WriteAllBytesAsync(string path, byte[] content, CancellationToken ct)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous,
        };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        await using var stream = new FileStream(path, options);
        await stream.WriteAsync(content, ct);
    }
}
