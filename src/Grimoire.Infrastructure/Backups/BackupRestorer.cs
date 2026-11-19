using Grimoire.Core.Interfaces;
using Microsoft.Data.Sqlite;

namespace Grimoire.Infrastructure.Backups;

/// <summary>Result of restoring a backup into a throwaway database and smoke-testing it.</summary>
public sealed record BackupVerification(
    string Source,
    int Applications,
    int SecretVersions,
    int GlobalSecretVersions,
    int ConfigurationEntries,
    bool SecretDecrypted
)
{
    public bool Passed =>
        Applications > 0 && SecretVersions + GlobalSecretVersions > 0 && SecretDecrypted;
}

/// <summary>
/// Restores and verifies encrypted backups. Used by the <c>--restore</c> and <c>--verify-backup</c>
/// command-line modes, which run with the web server stopped.
/// </summary>
public sealed class BackupRestorer(
    BackupOptions options,
    IReadOnlyList<IBackupTarget> targets,
    IEncryptionService encryption,
    string connectionString
)
{
    public const string Latest = "latest";

    /// <summary>
    /// Reads a backup: a file path, or <c>latest</c> for the newest backup across enabled targets
    /// (local first, as it is the fastest to read).
    /// </summary>
    public async Task<(string Source, byte[] Content)> LoadAsync(
        string source,
        CancellationToken ct
    )
    {
        if (!string.Equals(source, Latest, StringComparison.OrdinalIgnoreCase))
            return (Path.GetFullPath(source), await File.ReadAllBytesAsync(source, ct));

        if (targets.Count == 0)
            throw new InvalidOperationException(
                "'latest' needs at least one enabled backup target."
            );

        (IBackupTarget Target, string Name)? newest = null;
        foreach (var target in targets)
        {
            var name = (await target.ListAsync(ct))
                .Select(o => o.Name)
                .OrderByDescending(n => n, StringComparer.Ordinal)
                .FirstOrDefault();
            if (
                name is not null
                && (newest is null || string.CompareOrdinal(name, newest.Value.Name) > 0)
            )
                newest = (target, name);
        }

        if (newest is null)
            throw new FileNotFoundException("No backups found in any enabled target.");

        var (from, backupName) = newest.Value;
        return ($"{from.Name}:{backupName}", await from.ReadAsync(backupName, ct));
    }

    /// <summary>
    /// Replaces the live database with the backup. The current file (with its WAL/SHM, if any) is kept
    /// as <c>&lt;db&gt;.pre-restore-&lt;timestamp&gt;</c>. Refuses to overwrite unless <paramref name="force"/>.
    /// </summary>
    public async Task<string> RestoreAsync(string source, bool force, CancellationToken ct)
    {
        var databasePath = DatabasePath();
        if (File.Exists(databasePath) && !force)
            throw new InvalidOperationException(
                $"'{databasePath}' already exists. Stop the service and pass --force to replace it."
            );

        var (resolved, content) = await LoadAsync(source, ct);

        // Decrypt next to the live database so the final move is a same-filesystem rename.
        var directory = Path.GetDirectoryName(databasePath)!;
        Directory.CreateDirectory(directory);
        var staging = Path.Combine(directory, $".grimoire-restore-{Guid.NewGuid():N}.db");
        try
        {
            await WriteDecryptedAsync(content, staging, ct);
            ValidateDatabase(staging);

            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var existing = databasePath + suffix;
                if (File.Exists(existing))
                    File.Move(existing, $"{databasePath}.pre-restore-{stamp}{suffix}");
            }

            File.Move(staging, databasePath);
            return resolved;
        }
        finally
        {
            File.Delete(staging);
        }
    }

    /// <summary>
    /// Restore test: decrypts the backup into a throwaway directory, checks integrity, counts rows and
    /// decrypts one secret value with the master key. The live database is never touched.
    /// </summary>
    public async Task<BackupVerification> VerifyAsync(string source, CancellationToken ct)
    {
        var (resolved, content) = await LoadAsync(source, ct);
        var tempDir = Directory.CreateTempSubdirectory("grimoire-verify-");
        try
        {
            var path = Path.Combine(tempDir.FullName, "verify.db");
            await WriteDecryptedAsync(content, path, ct);
            ValidateDatabase(path);

            using var connection = Open(path);
            var encryptedValue = Scalar<string?>(
                connection,
                """
                SELECT EncryptedValue FROM SecretVersions
                UNION ALL
                SELECT EncryptedValue FROM GlobalSecretVersions
                LIMIT 1
                """
            );

            return new BackupVerification(
                resolved,
                Applications: Count(
                    connection,
                    "SELECT COUNT(*) FROM Applications WHERE IsDeleted = 0"
                ),
                SecretVersions: Count(connection, "SELECT COUNT(*) FROM SecretVersions"),
                GlobalSecretVersions: Count(
                    connection,
                    "SELECT COUNT(*) FROM GlobalSecretVersions"
                ),
                ConfigurationEntries: Count(
                    connection,
                    "SELECT COUNT(*) FROM ConfigurationEntries"
                ),
                SecretDecrypted: encryptedValue is not null && CanDecrypt(encryptedValue)
            );
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            tempDir.Delete(recursive: true);
        }
    }

    private async Task WriteDecryptedAsync(byte[] content, string path, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Key))
            throw new InvalidOperationException("'Backup:Key' is required to decrypt a backup.");

        byte[] plaintext;
        try
        {
            plaintext = new BackupCipher(options.Key).Decrypt(content);
        }
        catch (System.Security.Cryptography.AuthenticationTagMismatchException ex)
        {
            throw new System.Security.Cryptography.AuthenticationTagMismatchException(
                "Could not decrypt the backup: 'Backup:Key' is wrong or the file was modified.",
                ex
            );
        }
        await File.WriteAllBytesAsync(path, plaintext, ct);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    private static void ValidateDatabase(string path)
    {
        using var connection = Open(path);
        var integrity = Scalar<string>(connection, "PRAGMA integrity_check");
        if (!string.Equals(integrity, "ok", StringComparison.Ordinal))
            throw new InvalidDataException(
                $"Backup failed the SQLite integrity check: {integrity}"
            );

        var migrations = Count(
            connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '__EFMigrationsHistory'"
        );
        if (migrations == 0)
            throw new InvalidDataException(
                "Backup is not a Grimoire database (no migrations table)."
            );
    }

    private bool CanDecrypt(string encryptedValue)
    {
        try
        {
            encryption.Decrypt(encryptedValue);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private string DatabasePath()
    {
        var dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        if (string.IsNullOrWhiteSpace(dataSource) || dataSource == ":memory:")
            throw new InvalidOperationException(
                "ConnectionStrings:Default must point to a database file."
            );
        return Path.GetFullPath(dataSource);
    }

    private static SqliteConnection Open(string path)
    {
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()
        );
        connection.Open();
        return connection;
    }

    private static int Count(SqliteConnection connection, string sql) =>
        Convert.ToInt32(Scalar<long>(connection, sql));

    private static T Scalar<T>(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = command.ExecuteScalar();
        return result is null or DBNull ? default! : (T)result;
    }
}
