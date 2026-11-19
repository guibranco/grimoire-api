using System.Diagnostics;
using System.Security.Cryptography;
using Grimoire.Core.Backups;
using Grimoire.Core.Interfaces;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Grimoire.Infrastructure.Backups;

public sealed class BackupService(
    BackupOptions options,
    IReadOnlyList<IBackupTarget> targets,
    IConfiguration configuration,
    HealthcheckPinger pinger,
    ILogger<BackupService> logger,
    TimeProvider time
) : IBackupService, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public bool IsConfigured => options.Enabled && options.Validate().Count == 0;

    public async Task<BackupInfo> CreateBackupAsync(CancellationToken ct)
    {
        EnsureConfigured();
        await _gate.WaitAsync(ct);
        try
        {
            var info = await RunAsync(ct);
            await pinger.SuccessAsync(options.HealthcheckUrl, ct);
            return info;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Backup failed");
            await pinger.FailureAsync(options.HealthcheckUrl, CancellationToken.None);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<IReadOnlyList<BackupInfo>> ListBackupsAsync(CancellationToken ct)
    {
        EnsureConfigured();
        var byName = new Dictionary<string, (long Size, List<string> Targets)>(
            StringComparer.Ordinal
        );
        foreach (var target in targets)
        {
            foreach (var obj in await target.ListAsync(ct))
            {
                if (!byName.TryGetValue(obj.Name, out var entry))
                    byName[obj.Name] = entry = (obj.SizeBytes, []);
                entry.Targets.Add(target.Name);
            }
        }

        return byName
            .Select(kv =>
            {
                BackupNaming.TryParse(kv.Key, out var createdAt);
                return new BackupInfo(kv.Key, kv.Value.Size, createdAt, kv.Value.Targets);
            })
            .OrderByDescending(b => b.CreatedAt)
            .ToList();
    }

    private async Task<BackupInfo> RunAsync(CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        var name = BackupNaming.For(time.GetUtcNow());
        BackupNaming.TryParse(name, out var createdAt); // same millisecond precision as listings

        var snapshot = await TakeSnapshotAsync(ct);
        byte[] encrypted;
        try
        {
            encrypted = new BackupCipher(options.Key!).Encrypt(snapshot);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(snapshot);
        }

        var written = new List<string>();
        var failures = new List<Exception>();
        foreach (var target in targets)
        {
            try
            {
                await target.WriteAsync(name, encrypted, ct);
                written.Add(target.Name);
                await ApplyRetentionAsync(target, name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(
                    ex,
                    "Writing backup {Name} to target {Target} failed",
                    name,
                    target.Name
                );
                failures.Add(ex);
            }
        }

        // A backup only counts when every target has it: a silently missing off-site copy is
        // exactly the failure this feature exists to catch, so it must reach the healthcheck.
        if (failures.Count > 0)
            throw new AggregateException(
                $"Backup {name} failed on {failures.Count} target(s).",
                failures
            );

        logger.LogInformation(
            "Backup {Name} ({Size} bytes) written to {Targets} in {ElapsedMs} ms",
            name,
            encrypted.Length,
            string.Join(", ", written),
            stopwatch.ElapsedMilliseconds
        );
        return new BackupInfo(name, encrypted.Length, createdAt, written);
    }

    /// <summary>
    /// Consistent online snapshot with <c>VACUUM INTO</c>: readers and writers keep working, and the
    /// copy is a single, compacted file with no WAL to carry along.
    /// </summary>
    private async Task<byte[]> TakeSnapshotAsync(CancellationToken ct)
    {
        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

        var tempDir = Directory.CreateTempSubdirectory("grimoire-backup-");
        try
        {
            var snapshotPath = Path.Combine(tempDir.FullName, "snapshot.db");
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync(ct);
                await using var command = connection.CreateCommand();
                command.CommandText = "VACUUM INTO $path";
                command.Parameters.AddWithValue("$path", snapshotPath);
                await command.ExecuteNonQueryAsync(ct);
            }

            return await File.ReadAllBytesAsync(snapshotPath, ct);
        }
        finally
        {
            tempDir.Delete(recursive: true);
        }
    }

    private async Task ApplyRetentionAsync(
        IBackupTarget target,
        string justWritten,
        CancellationToken ct
    )
    {
        // The backup just written always survives, even if a backward clock step gave it an older
        // name than an existing one; it counts towards the retention total.
        var expired = (await target.ListAsync(ct))
            .Select(o => o.Name)
            .Where(n => n != justWritten)
            .OrderByDescending(n => n, StringComparer.Ordinal)
            .Skip(options.RetentionCount - 1);

        foreach (var name in expired)
        {
            await target.DeleteAsync(name, ct);
            logger.LogInformation(
                "Retention: deleted backup {Name} from {Target}",
                name,
                target.Name
            );
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
            throw new BackupNotConfiguredException();
    }

    public void Dispose() => _gate.Dispose();
}

public sealed class BackupNotConfiguredException()
    : InvalidOperationException(
        "Backups are not configured. Set 'Backup:Enabled', 'Backup:Key' and at least one target."
    );
