using Grimoire.Core.Backups;

namespace Grimoire.Core.Interfaces;

public interface IBackupService
{
    /// <summary>True when backups are enabled, a backup key is set and at least one target is enabled.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Takes a consistent snapshot of the database, encrypts it with the backup key, writes it to
    /// every enabled target and applies the retention policy. Concurrent calls run one at a time.
    /// </summary>
    Task<BackupInfo> CreateBackupAsync(CancellationToken ct);

    /// <summary>Lists the backups found in every enabled target, newest first.</summary>
    Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct);
}
