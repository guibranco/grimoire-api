using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Grimoire.Infrastructure.Persistence;

public sealed class ConfigurationChangeContext : IConfigurationChangeContext
{
    public string Actor { get; set; } = "admin";
    public int? RollbackOf { get; set; }
}

/// <summary>
/// Appends a <see cref="ConfigurationRevision"/> for every added, changed (value, type or
/// description) or deleted configuration entry, in the same <c>SaveChanges</c> as the change.
/// </summary>
public sealed class ConfigurationHistoryInterceptor(
    IConfigurationChangeContext change,
    TimeProvider time
) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result
    )
    {
        if (eventData.Context is GrimoireDbContext db)
            AppendRevisionsAsync(db, CancellationToken.None).GetAwaiter().GetResult();
        return result;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        if (eventData.Context is GrimoireDbContext db)
            await AppendRevisionsAsync(db, cancellationToken);
        return result;
    }

    private sealed record Pending(
        ConfigurationScope Scope,
        Guid? ApplicationId,
        string EnvironmentKey,
        string Key,
        string Change,
        string? Value,
        ConfigurationValueType ValueType,
        string? Description
    );

    private async Task AppendRevisionsAsync(GrimoireDbContext db, CancellationToken ct)
    {
        var pending = db
            .ChangeTracker.Entries<ConfigurationEntry>()
            .Select(FromApplicationEntry)
            .Concat(db.ChangeTracker.Entries<GlobalConfigurationEntry>().Select(FromGlobalEntry))
            .OfType<Pending>()
            .ToList();
        if (pending.Count == 0)
            return;

        var now = time.GetUtcNow();
        var next = new Dictionary<(ConfigurationScope, string, string), int>();
        foreach (var p in pending)
        {
            var id = (p.Scope, p.EnvironmentKey, p.Key);
            if (!next.TryGetValue(id, out var revision))
                revision =
                    (
                        await db
                            .ConfigurationRevisions.Where(r =>
                                r.Scope == p.Scope
                                && r.EnvironmentKey == p.EnvironmentKey
                                && r.Key == p.Key
                            )
                            .MaxAsync(r => (int?)r.Revision, ct)
                        ?? 0
                    ) + 1;
            next[id] = revision + 1;

            var isRollback =
                change.RollbackOf is not null && p.Change != ConfigurationChanges.Delete;
            db.ConfigurationRevisions.Add(
                new ConfigurationRevision
                {
                    Id = Guid.NewGuid(),
                    Scope = p.Scope,
                    ApplicationId = p.ApplicationId,
                    EnvironmentKey = p.EnvironmentKey,
                    Key = p.Key,
                    Revision = revision,
                    Change = isRollback ? ConfigurationChanges.Rollback : p.Change,
                    Value = p.Value,
                    ValueType = p.ValueType,
                    Description = p.Description,
                    RolledBackFrom = isRollback ? change.RollbackOf : null,
                    ChangedBy = change.Actor,
                    ChangedAt = now,
                }
            );
        }
    }

    private static Pending? FromApplicationEntry(EntityEntry<ConfigurationEntry> e) =>
        ChangeOf(e) is { } kind
            ? new Pending(
                ConfigurationScope.Application,
                e.Entity.ApplicationId,
                ConfigurationRevision.EnvironmentKeyFor(e.Entity.EnvironmentId),
                e.Entity.Key,
                kind,
                kind == ConfigurationChanges.Delete ? null : e.Entity.Value,
                e.Entity.ValueType,
                e.Entity.Description
            )
            : null;

    private static Pending? FromGlobalEntry(EntityEntry<GlobalConfigurationEntry> e) =>
        ChangeOf(e) is { } kind
            ? new Pending(
                ConfigurationScope.Global,
                null,
                e.Entity.EnvironmentSlug,
                e.Entity.Key,
                kind,
                kind == ConfigurationChanges.Delete ? null : e.Entity.Value,
                e.Entity.ValueType,
                e.Entity.Description
            )
            : null;

    private static readonly string[] TrackedProperties = ["Value", "ValueType", "Description"];

    private static string? ChangeOf(EntityEntry entry) =>
        entry.State switch
        {
            EntityState.Added => ConfigurationChanges.Create,
            EntityState.Deleted => ConfigurationChanges.Delete,
            EntityState.Modified when TrackedProperties.Any(p => entry.Property(p).IsModified) =>
                ConfigurationChanges.Update,
            _ => null,
        };
}
