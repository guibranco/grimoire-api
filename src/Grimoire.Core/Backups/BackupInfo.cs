namespace Grimoire.Core.Backups;

/// <summary>An encrypted database snapshot, as stored in one or more backup targets.</summary>
/// <param name="Name">File/object name, e.g. <c>grimoire-20261119T030000123Z.gbak</c>.</param>
/// <param name="SizeBytes">Size of the encrypted file.</param>
/// <param name="CreatedAt">When the snapshot was taken (parsed from <paramref name="Name"/>).</param>
/// <param name="Targets">Names of the targets holding a copy (<c>local</c>, <c>s3</c>).</param>
public sealed record BackupInfo(
    string Name,
    long SizeBytes,
    DateTimeOffset CreatedAt,
    IReadOnlyList<string> Targets
);
