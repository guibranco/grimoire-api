namespace Grimoire.Infrastructure.Backups;

/// <summary>A file (as stored in a target) with its size.</summary>
public sealed record BackupObject(string Name, long SizeBytes);

/// <summary>A place encrypted backups are written to.</summary>
public interface IBackupTarget
{
    /// <summary>Short name shown in listings: <c>local</c> or <c>s3</c>.</summary>
    string Name { get; }

    Task WriteAsync(string name, byte[] content, CancellationToken ct);

    /// <summary>Backups only (names matching <see cref="BackupNaming"/>); other files are ignored.</summary>
    Task<IReadOnlyList<BackupObject>> ListAsync(CancellationToken ct);

    Task<byte[]> ReadAsync(string name, CancellationToken ct);

    Task DeleteAsync(string name, CancellationToken ct);
}
