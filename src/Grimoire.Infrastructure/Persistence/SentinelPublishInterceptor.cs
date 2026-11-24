using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

namespace Grimoire.Infrastructure.Persistence;

/// <summary>
/// Keeps the sentinel keys in step with changes, in the same <c>SaveChanges</c> as the change
/// (guibranco/grimoire-api#95):
/// <list type="bullet">
/// <item>A configuration or secret-version change in an application environment bumps that
/// environment's <see cref="ConfigurationSentinel.ApplicationKey"/> when it auto-publishes, or
/// marks it as having pending changes when it doesn't.</item>
/// <item>A global configuration or global secret-version change does the same for
/// <see cref="ConfigurationSentinel.GlobalKey"/> at the <c>*</c> layer.</item>
/// <item>Turning an application's global inheritance on or off changes what all its
/// environments see, so it counts as a change in each of them.</item>
/// <item>Deleting a secret or a global secret removes its versions, so it counts too.</item>
/// <item>Writing a sentinel directly is a publish: it clears the pending changes.</item>
/// </list>
/// The decision (auto-publish or not, the next sentinel value) is made under the database
/// write lock: when the save isn't already in a transaction, the interceptor opens one before
/// reading and commits it after the save, so concurrent saves can't publish stale state.
/// Registered before <see cref="ConfigurationHistoryInterceptor"/>, so a bump is also a
/// revision in the sentinel key's history.
/// </summary>
public sealed class SentinelPublishInterceptor(TimeProvider time) : SaveChangesInterceptor
{
    private static readonly string[] ConfigurationProperties =
    [
        "Value",
        "ValueType",
        "Description",
    ];
    private static readonly string[] SecretVersionProperties =
    [
        "IsEnabled",
        "NotBefore",
        "ExpiresAt",
    ];

    /// <summary>The write transaction this interceptor opened for the current save, if any.</summary>
    private IDbContextTransaction? _transaction;

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        SavingChangesAsync(eventData, result).AsTask().GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is not GrimoireDbContext db || !HasCandidates(db))
            return result;

        if (db.Database.CurrentTransaction is null)
            _transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await PublishAsync(db, cancellationToken);
        }
        catch
        {
            await EndAsync(commit: false, CancellationToken.None);
            throw;
        }
        return result;
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        EndAsync(commit: true, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        await EndAsync(commit: true, cancellationToken);
        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
        EndAsync(commit: false, CancellationToken.None).GetAwaiter().GetResult();

    public override Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default
    ) => EndAsync(commit: false, CancellationToken.None);

    public override void SaveChangesCanceled(DbContextEventData eventData) =>
        EndAsync(commit: false, CancellationToken.None).GetAwaiter().GetResult();

    public override Task SaveChangesCanceledAsync(
        DbContextEventData eventData,
        CancellationToken cancellationToken = default
    ) => EndAsync(commit: false, CancellationToken.None);

    private async Task EndAsync(bool commit, CancellationToken ct)
    {
        if (_transaction is not { } transaction)
            return;
        _transaction = null;
        try
        {
            if (commit)
                await transaction.CommitAsync(ct);
            else
                await transaction.RollbackAsync(CancellationToken.None);
        }
        finally
        {
            await transaction.DisposeAsync();
        }
    }

    /// <summary>Anything in this save that can change what clients read.</summary>
    private static bool HasCandidates(GrimoireDbContext db) =>
        db
            .ChangeTracker.Entries()
            .Any(e =>
                e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                && e.Entity
                    is ConfigurationEntry
                        or GlobalConfigurationEntry
                        or SecretVersion
                        or GlobalSecretVersion
                        or Secret
                        or GlobalSecret
                        or Application
            );

    private async Task PublishAsync(GrimoireDbContext db, CancellationToken ct)
    {
        var changes = await CollectAsync(db, ct);
        if (changes.IsEmpty)
            return;

        var now = time.GetUtcNow();
        foreach (var environmentId in changes.ChangedEnvironments)
            if (!changes.PublishedEnvironments.Contains(environmentId))
                await ApplyEnvironmentChangeAsync(db, environmentId, now, ct);

        foreach (var environmentId in changes.PublishedEnvironments)
            if (await LiveEnvironmentAsync(db, environmentId, ct) is { } env)
                env.PendingChangesSince = null;

        if (changes.GlobalPublished)
            await MarkGlobalPublishedAsync(db, now, ct);
        else if (changes.GlobalChanged)
            await ApplyGlobalChangeAsync(db, now, ct);
    }

    /// <summary>
    /// The tracked environment with its publishing state as committed now (under the write
    /// lock), unless this save changes it; <c>null</c> when it is gone or being deleted.
    /// </summary>
    private static async Task<AppEnvironment?> LiveEnvironmentAsync(
        GrimoireDbContext db,
        Guid environmentId,
        CancellationToken ct
    )
    {
        var env = await db.Environments.FindAsync([environmentId], ct);
        if (env is null)
            return null;
        var entry = db.Entry(env);
        if (entry.State == EntityState.Deleted)
            return null;
        if (entry.State == EntityState.Added)
            return env;

        var committed = await db
            .Environments.AsNoTracking()
            .Where(e => e.Id == environmentId)
            .Select(e => new { e.AutoPublish, e.PendingChangesSince })
            .FirstOrDefaultAsync(ct);
        if (committed is null)
            return null;
        Refresh(entry.Property(e => e.AutoPublish), committed.AutoPublish);
        Refresh(entry.Property(e => e.PendingChangesSince), committed.PendingChangesSince);
        return env;
    }

    /// <summary>Same, for the global settings row; <c>null</c> when there is none (auto-publish).</summary>
    private static async Task<GlobalPublishing?> LiveGlobalSettingsAsync(
        GrimoireDbContext db,
        CancellationToken ct
    )
    {
        var settings = await db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct);
        if (settings is null)
            return null;
        var entry = db.Entry(settings);
        if (entry.State == EntityState.Added)
            return settings;

        var committed = await db
            .GlobalPublishing.AsNoTracking()
            .Where(g => g.Id == GlobalPublishing.SingletonId)
            .Select(g => new { g.AutoPublish, g.PendingChangesSince })
            .FirstOrDefaultAsync(ct);
        if (committed is null)
            return null;
        Refresh(entry.Property(g => g.AutoPublish), committed.AutoPublish);
        Refresh(entry.Property(g => g.PendingChangesSince), committed.PendingChangesSince);
        return settings;
    }

    /// <summary>Overwrite a stale tracked value with the committed one, unless this save sets it.</summary>
    private static void Refresh<T>(PropertyEntry<AppEnvironment, T> property, T committed)
    {
        if (property.IsModified)
            return;
        property.OriginalValue = committed;
        property.CurrentValue = committed;
        property.IsModified = false;
    }

    private static void Refresh<T>(PropertyEntry<GlobalPublishing, T> property, T committed)
    {
        if (property.IsModified)
            return;
        property.OriginalValue = committed;
        property.CurrentValue = committed;
        property.IsModified = false;
    }

    /// <summary>Auto-publish: bump the sentinel. Otherwise: remember there are pending changes.</summary>
    private static async Task ApplyEnvironmentChangeAsync(
        GrimoireDbContext db,
        Guid environmentId,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (await LiveEnvironmentAsync(db, environmentId, ct) is not { } env)
            return;
        if (!env.AutoPublish)
        {
            env.PendingChangesSince ??= now;
            return;
        }
        await SentinelWriter.BumpApplicationAsync(db, env, now, ct);
        env.PendingChangesSince = null;
    }

    private static async Task MarkGlobalPublishedAsync(
        GrimoireDbContext db,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (await LiveGlobalSettingsAsync(db, ct) is not { } settings)
            return;
        settings.PendingChangesSince = null;
        settings.UpdatedAt = now;
    }

    /// <summary>No settings row means auto-publish, the default.</summary>
    private static async Task ApplyGlobalChangeAsync(
        GrimoireDbContext db,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var settings = await LiveGlobalSettingsAsync(db, ct);
        if (settings is { AutoPublish: false })
        {
            settings.PendingChangesSince ??= now;
            settings.UpdatedAt = now;
            return;
        }
        await SentinelWriter.BumpGlobalAsync(db, now, ct);
        if (settings is not null)
        {
            settings.PendingChangesSince = null;
            settings.UpdatedAt = now;
        }
    }

    private sealed class Changes
    {
        public HashSet<Guid> ChangedEnvironments { get; } = [];
        public HashSet<Guid> PublishedEnvironments { get; } = [];
        public bool GlobalChanged { get; set; }
        public bool GlobalPublished { get; set; }

        public bool IsEmpty =>
            ChangedEnvironments.Count == 0
            && PublishedEnvironments.Count == 0
            && !GlobalChanged
            && !GlobalPublished;
    }

    private static async Task<Changes> CollectAsync(GrimoireDbContext db, CancellationToken ct)
    {
        var changes = new Changes();
        CollectApplicationChanges(db, changes);
        CollectGlobalChanges(db, changes);
        changes.ChangedEnvironments.UnionWith(await DeletedSecretEnvironmentsAsync(db, ct));
        changes.ChangedEnvironments.UnionWith(await InheritanceChangedEnvironmentsAsync(db, ct));
        return changes;
    }

    private static void CollectApplicationChanges(GrimoireDbContext db, Changes changes)
    {
        var configurations = db
            .ChangeTracker.Entries<ConfigurationEntry>()
            .Where(e => IsChange(e, ConfigurationProperties))
            .Select(e => e.Entity);
        foreach (var entry in configurations)
        {
            var target =
                entry.Key == ConfigurationSentinel.ApplicationKey
                    ? changes.PublishedEnvironments
                    : changes.ChangedEnvironments;
            target.Add(entry.EnvironmentId);
        }

        changes.ChangedEnvironments.UnionWith(
            db.ChangeTracker.Entries<SecretVersion>()
                .Where(e => IsChange(e, SecretVersionProperties))
                .Select(e => e.Entity.EnvironmentId)
        );
    }

    private static void CollectGlobalChanges(GrimoireDbContext db, Changes changes)
    {
        var entries = db
            .ChangeTracker.Entries<GlobalConfigurationEntry>()
            .Where(e => IsChange(e, ConfigurationProperties))
            .Select(e => e.Entity)
            .ToList();
        // Only the * layer is the global sentinel (validators keep it there).
        changes.GlobalPublished = entries.Exists(IsGlobalSentinel);
        changes.GlobalChanged =
            entries.Exists(e => !IsGlobalSentinel(e))
            || db.ChangeTracker.Entries<GlobalSecretVersion>()
                .Any(e => IsChange(e, SecretVersionProperties))
            || db.ChangeTracker.Entries<GlobalSecret>().Any(e => e.State == EntityState.Deleted);
    }

    private static bool IsGlobalSentinel(GlobalConfigurationEntry e) =>
        e.Key == ConfigurationSentinel.GlobalKey
        && e.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments;

    /// <summary>
    /// Environments where a deleted secret had versions: the database removes them by cascade,
    /// so they never show up as tracked deletes.
    /// </summary>
    private static async Task<List<Guid>> DeletedSecretEnvironmentsAsync(
        GrimoireDbContext db,
        CancellationToken ct
    )
    {
        var secretIds = db
            .ChangeTracker.Entries<Secret>()
            .Where(e => e.State == EntityState.Deleted)
            .Select(e => e.Entity.Id)
            .ToList();
        if (secretIds.Count == 0)
            return [];
        return await db
            .SecretVersions.AsNoTracking()
            .Where(v => secretIds.Contains(v.SecretId))
            .Select(v => v.EnvironmentId)
            .Distinct()
            .ToListAsync(ct);
    }

    /// <summary>Environments of applications whose global inheritance was switched.</summary>
    private static async Task<List<Guid>> InheritanceChangedEnvironmentsAsync(
        GrimoireDbContext db,
        CancellationToken ct
    )
    {
        var applicationIds = db
            .ChangeTracker.Entries<Application>()
            .Where(e =>
                e.State == EntityState.Modified
                && e.Property(a => a.InheritGlobals).IsModified
                && !Equals(
                    e.Property(a => a.InheritGlobals).OriginalValue,
                    e.Property(a => a.InheritGlobals).CurrentValue
                )
            )
            .Select(e => e.Entity.Id)
            .ToList();
        if (applicationIds.Count == 0)
            return [];
        return await db
            .Environments.Where(e => applicationIds.Contains(e.ApplicationId))
            .Select(e => e.Id)
            .ToListAsync(ct);
    }

    private static bool IsChange(EntityEntry entry, string[] properties) =>
        entry.State switch
        {
            EntityState.Added or EntityState.Deleted => true,
            EntityState.Modified => properties.Any(p => entry.Property(p).IsModified),
            _ => false,
        };
}
