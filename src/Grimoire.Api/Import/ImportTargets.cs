using Grimoire.Core.Entities;
using Grimoire.Core.Import;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Secrets;

namespace Grimoire.Api.Import;

/// <summary>Where an import is written: an application environment or a global environment.</summary>
public interface IImportTarget
{
    /// <summary>What already exists for <paramref name="keys"/> in the target environment.</summary>
    Task<ImportSnapshot> SnapshotAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset now,
        CancellationToken ct
    );

    /// <summary>Writes one planned key (create or update). Call <see cref="SnapshotAsync"/> first.</summary>
    Task ApplyAsync(ImportPlanItem item, DateTimeOffset now, CancellationToken ct);

    /// <summary>Runs <paramref name="work"/> in one write transaction.</summary>
    Task<T> InTransactionAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct);
}

public sealed class ApplicationImportTarget(
    Application app,
    AppEnvironment env,
    IConfigurationRepository configs,
    ISecretRepository secrets,
    IEncryptionService encryption
) : IImportTarget
{
    private Dictionary<string, ConfigurationEntry> _entries = [];
    private Dictionary<string, Secret> _secrets = [];

    public async Task<ImportSnapshot> SnapshotAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        _entries = (await configs.GetByEnvironmentAsync(app.Id, env.Id, ct))
            .Where(e => wanted.Contains(e.Key))
            .ToDictionary(e => e.Key, StringComparer.Ordinal);
        _secrets = (await secrets.GetByApplicationAsync(app.Id, ct))
            .Where(s => wanted.Contains(s.Name))
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, secret) in _secrets)
        {
            var current = SecretVersionRules.Current(
                await secrets.GetVersionsAsync(secret.Id, env.Id, ct),
                now
            );
            values[name] = current is null ? null : encryption.Decrypt(current.EncryptedValue);
        }
        return new ImportSnapshot(
            _entries.ToDictionary(
                e => e.Key,
                e => (e.Value.Value, e.Value.ValueType),
                StringComparer.Ordinal
            ),
            values
        );
    }

    public async Task ApplyAsync(ImportPlanItem item, DateTimeOffset now, CancellationToken ct)
    {
        if (item.Kind == ImportKind.Secret)
        {
            await ApplySecretAsync(item, now, ct);
            return;
        }

        if (_entries.TryGetValue(item.Key, out var entry))
        {
            entry.Value = item.Value;
            entry.ValueType = item.Type;
            entry.UpdatedAt = now;
            await configs.UpdateAsync(entry, ct);
            return;
        }
        await configs.AddAsync(
            new ConfigurationEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                EnvironmentId = env.Id,
                Key = item.Key,
                Value = item.Value,
                ValueType = item.Type,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct
        );
    }

    private async Task ApplySecretAsync(
        ImportPlanItem item,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (!_secrets.TryGetValue(item.Key, out var secret))
        {
            secret = new Secret
            {
                Id = Guid.NewGuid(),
                ApplicationId = app.Id,
                Name = item.Key,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await secrets.AddAsync(secret, ct);
            _secrets[item.Key] = secret;
        }
        await secrets.AddVersionAsync(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secret.Id,
                EnvironmentId = env.Id,
                EncryptedValue = encryption.Encrypt(item.Value),
                IsEnabled = true,
                Version = await secrets.GetNextVersionNumberAsync(secret.Id, env.Id, ct),
                CreatedAt = now,
            },
            ct: ct
        );
    }

    public Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct
    ) => secrets.InWriteTransactionAsync(work, ct);
}

public sealed class GlobalImportTarget(
    string environmentSlug,
    IGlobalConfigurationRepository configs,
    IGlobalSecretRepository secrets,
    IEncryptionService encryption
) : IImportTarget
{
    private Dictionary<string, GlobalConfigurationEntry> _entries = [];
    private Dictionary<string, GlobalSecret> _secrets = [];

    public async Task<ImportSnapshot> SnapshotAsync(
        IReadOnlyCollection<string> keys,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var wanted = keys.ToHashSet(StringComparer.Ordinal);
        _entries = (await configs.GetAllAsync(environmentSlug, ct))
            .Where(e => wanted.Contains(e.Key))
            .ToDictionary(e => e.Key, StringComparer.Ordinal);
        _secrets = (await secrets.GetAllAsync(ct))
            .Where(s => wanted.Contains(s.Name))
            .ToDictionary(s => s.Name, StringComparer.Ordinal);

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (name, secret) in _secrets)
        {
            // Only this exact environment: a "*" value isn't this environment's own.
            var current = SecretVersionRules.Current(
                await secrets.GetVersionsAsync(secret.Id, environmentSlug, ct),
                now
            );
            values[name] = current is null ? null : encryption.Decrypt(current.EncryptedValue);
        }
        return new ImportSnapshot(
            _entries.ToDictionary(
                e => e.Key,
                e => (e.Value.Value, e.Value.ValueType),
                StringComparer.Ordinal
            ),
            values
        );
    }

    public async Task ApplyAsync(ImportPlanItem item, DateTimeOffset now, CancellationToken ct)
    {
        if (item.Kind == ImportKind.Secret)
        {
            await ApplySecretAsync(item, now, ct);
            return;
        }

        if (_entries.TryGetValue(item.Key, out var entry))
        {
            entry.Value = item.Value;
            entry.ValueType = item.Type;
            entry.UpdatedAt = now;
            await configs.UpdateAsync(entry, ct);
            return;
        }
        await configs.AddAsync(
            new GlobalConfigurationEntry
            {
                Id = Guid.NewGuid(),
                EnvironmentSlug = environmentSlug,
                Key = item.Key,
                Value = item.Value,
                ValueType = item.Type,
                CreatedAt = now,
                UpdatedAt = now,
            },
            ct
        );
    }

    private async Task ApplySecretAsync(
        ImportPlanItem item,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (!_secrets.TryGetValue(item.Key, out var secret))
        {
            secret = new GlobalSecret
            {
                Id = Guid.NewGuid(),
                Name = item.Key,
                CreatedAt = now,
                UpdatedAt = now,
            };
            await secrets.AddAsync(secret, ct);
            _secrets[item.Key] = secret;
        }
        await secrets.AddVersionAsync(
            new GlobalSecretVersion
            {
                Id = Guid.NewGuid(),
                GlobalSecretId = secret.Id,
                EnvironmentSlug = environmentSlug,
                EncryptedValue = encryption.Encrypt(item.Value),
                IsEnabled = true,
                Version = await secrets.GetNextVersionNumberAsync(secret.Id, environmentSlug, ct),
                CreatedAt = now,
            },
            ct: ct
        );
    }

    public Task<T> InTransactionAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken ct
    ) => secrets.InWriteTransactionAsync(work, ct);
}
