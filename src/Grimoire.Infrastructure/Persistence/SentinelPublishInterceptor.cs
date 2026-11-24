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
        {
            if (changes.PublishedEnvironments.Contains(environmentId))
                continue;
            var env = await db.Environments.FindAsync([environmentId], ct);
            if (env is null || db.Entry(env).State == EntityState.Deleted)
                continue;
            if (env.AutoPublish)
            {
                await SentinelWriter.BumpApplicationAsync(db, env, now, ct);
                env.PendingChangesSince = null;
            }
            else
            {
                env.PendingChangesSince ??= now;
            }
        }

        foreach (var environmentId in changes.PublishedEnvironments)
        {
            var env = await db.Environments.FindAsync([environmentId], ct);
            if (env is not null && db.Entry(env).State != EntityState.Deleted)
                env.PendingChangesSince = null;
        }

        if (changes.GlobalPublished)
        {
            var settings = await db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct);
            if (settings is not null)
            {
                settings.PendingChangesSince = null;
                settings.UpdatedAt = now;
            }
        }
        else if (changes.GlobalChanged)
        {
            var settings = await db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct);
            if (settings is null || settings.AutoPublish)
            {
                await SentinelWriter.BumpGlobalAsync(db, now, ct);
                if (settings is not null)
                {
                    settings.PendingChangesSince = null;
                    settings.UpdatedAt = now;
                }
            }
            else
            {
                settings.PendingChangesSince ??= now;
                settings.UpdatedAt = now;
            }
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

        foreach (var entry in db.ChangeTracker.Entries<ConfigurationEntry>())
        {
            if (!IsChange(entry, ConfigurationProperties))
                continue;
            if (entry.Entity.Key == ConfigurationSentinel.ApplicationKey)
                changes.PublishedEnvironments.Add(entry.Entity.EnvironmentId);
            else
                changes.ChangedEnvironments.Add(entry.Entity.EnvironmentId);
        }

        foreach (var entry in db.ChangeTracker.Entries<SecretVersion>())
            if (IsChange(entry, SecretVersionProperties))
                changes.ChangedEnvironments.Add(entry.Entity.EnvironmentId);

        foreach (var entry in db.ChangeTracker.Entries<GlobalConfigurationEntry>())
        {
            if (!IsChange(entry, ConfigurationProperties))
                continue;
            if (entry.Entity.Key == ConfigurationSentinel.GlobalKey)
                changes.GlobalPublished = true;
            else
                changes.GlobalChanged = true;
        }

        if (
            db
                .ChangeTracker.Entries<GlobalSecretVersion>()
                .Any(e => IsChange(e, SecretVersionProperties))
        )
            changes.GlobalChanged = true;

        var inheritanceChanged = db
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
        if (inheritanceChanged.Count > 0)
        {
            var environmentIds = await db
                .Environments.Where(e => inheritanceChanged.Contains(e.ApplicationId))
                .Select(e => e.Id)
                .ToListAsync(ct);
            changes.ChangedEnvironments.UnionWith(environmentIds);
        }

        return changes;
    }

    private static bool IsChange(EntityEntry entry, string[] properties) =>
        entry.State switch
        {
            EntityState.Added or EntityState.Deleted => true,
            EntityState.Modified => properties.Any(p => entry.Property(p).IsModified),
            _ => false,
        };
}
