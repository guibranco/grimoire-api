using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
/// <item>Writing a sentinel directly is a publish: it clears the pending changes.</item>
/// </list>
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

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is GrimoireDbContext db)
            PublishAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is GrimoireDbContext db)
            await PublishAsync(db, cancellationToken);
        return result;
    }

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
            if (await FindLiveEnvironmentAsync(db, environmentId, ct) is { } env)
                env.PendingChangesSince = null;

        if (changes.GlobalPublished)
            await MarkGlobalPublishedAsync(db, now, ct);
        else if (changes.GlobalChanged)
            await ApplyGlobalChangeAsync(db, now, ct);
    }

    private static async Task<AppEnvironment?> FindLiveEnvironmentAsync(
        GrimoireDbContext db,
        Guid environmentId,
        CancellationToken ct
    )
    {
        var env = await db.Environments.FindAsync([environmentId], ct);
        return env is null || db.Entry(env).State == EntityState.Deleted ? null : env;
    }

    /// <summary>Auto-publish: bump the sentinel. Otherwise: remember there are pending changes.</summary>
    private static async Task ApplyEnvironmentChangeAsync(
        GrimoireDbContext db,
        Guid environmentId,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        if (await FindLiveEnvironmentAsync(db, environmentId, ct) is not { } env)
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
        var settings = await db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct);
        if (settings is null)
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
        var settings = await db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct);
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
        var keys = db
            .ChangeTracker.Entries<GlobalConfigurationEntry>()
            .Where(e => IsChange(e, ConfigurationProperties))
            .Select(e => e.Entity.Key)
            .ToList();
        changes.GlobalPublished = keys.Contains(ConfigurationSentinel.GlobalKey);
        changes.GlobalChanged =
            keys.Exists(k => k != ConfigurationSentinel.GlobalKey)
            || db.ChangeTracker.Entries<GlobalSecretVersion>()
                .Any(e => IsChange(e, SecretVersionProperties));
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
