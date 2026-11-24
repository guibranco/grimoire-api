using Grimoire.Core.Entities;
using Grimoire.Core.Publishing;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Infrastructure.Services;

/// <summary>
/// Publishing writes the sentinel; <see cref="SentinelPublishInterceptor"/> then clears the
/// pending changes in the same <c>SaveChanges</c>, exactly as for a direct write of the key.
/// </summary>
public sealed class PublishingService(GrimoireDbContext db, TimeProvider time) : IPublishingService
{
    public async Task<PublishingStatus> GetAsync(
        AppEnvironment environment,
        CancellationToken ct = default
    )
    {
        var env = await LoadAsync(environment, ct);
        return await StatusAsync(env, ct);
    }

    public async Task<PublishingStatus> SetAutoPublishAsync(
        AppEnvironment environment,
        bool autoPublish,
        CancellationToken ct = default
    )
    {
        var env = await LoadAsync(environment, ct);
        var now = time.GetUtcNow();
        env.AutoPublish = autoPublish;
        env.UpdatedAt = now;
        if (autoPublish && env.PendingChangesSince is not null)
            await SentinelWriter.BumpApplicationAsync(db, env, now, ct);
        await db.SaveChangesAsync(ct);
        return await StatusAsync(env, ct);
    }

    public async Task<PublishingStatus> PublishAsync(
        AppEnvironment environment,
        CancellationToken ct = default
    )
    {
        var env = await LoadAsync(environment, ct);
        await SentinelWriter.BumpApplicationAsync(db, env, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return await StatusAsync(env, ct);
    }

    public async Task<PublishingStatus> GetGlobalAsync(CancellationToken ct = default) =>
        await GlobalStatusAsync(await FindGlobalAsync(ct), ct);

    public async Task<PublishingStatus> SetGlobalAutoPublishAsync(
        bool autoPublish,
        CancellationToken ct = default
    )
    {
        var now = time.GetUtcNow();
        var settings = await FindGlobalAsync(ct);
        if (settings is null)
        {
            settings = new GlobalPublishing();
            db.GlobalPublishing.Add(settings);
        }
        settings.AutoPublish = autoPublish;
        settings.UpdatedAt = now;
        if (autoPublish && settings.PendingChangesSince is not null)
            await SentinelWriter.BumpGlobalAsync(db, now, ct);
        await db.SaveChangesAsync(ct);
        return await GlobalStatusAsync(settings, ct);
    }

    public async Task<PublishingStatus> PublishGlobalAsync(CancellationToken ct = default)
    {
        await SentinelWriter.BumpGlobalAsync(db, time.GetUtcNow(), ct);
        await db.SaveChangesAsync(ct);
        return await GetGlobalAsync(ct);
    }

    private async Task<AppEnvironment> LoadAsync(
        AppEnvironment environment,
        CancellationToken ct
    ) =>
        await db.Environments.FindAsync([environment.Id], ct)
        ?? throw new InvalidOperationException($"Environment {environment.Id} not found.");

    private Task<GlobalPublishing?> FindGlobalAsync(CancellationToken ct) =>
        db.GlobalPublishing.FindAsync([GlobalPublishing.SingletonId], ct).AsTask();

    private async Task<PublishingStatus> StatusAsync(AppEnvironment env, CancellationToken ct)
    {
        var sentinel = await db
            .ConfigurationEntries.AsNoTracking()
            .FirstOrDefaultAsync(
                e => e.EnvironmentId == env.Id && e.Key == ConfigurationSentinel.ApplicationKey,
                ct
            );
        return new PublishingStatus(
            env.AutoPublish,
            ConfigurationSentinel.ApplicationKey,
            sentinel?.Value,
            sentinel?.UpdatedAt,
            env.PendingChangesSince
        );
    }

    private async Task<PublishingStatus> GlobalStatusAsync(
        GlobalPublishing? settings,
        CancellationToken ct
    )
    {
        var sentinel = await db
            .GlobalConfigurationEntries.AsNoTracking()
            .FirstOrDefaultAsync(
                e =>
                    e.EnvironmentSlug == GlobalConfigurationEntry.AllEnvironments
                    && e.Key == ConfigurationSentinel.GlobalKey,
                ct
            );
        return new PublishingStatus(
            settings?.AutoPublish ?? true,
            ConfigurationSentinel.GlobalKey,
            sentinel?.Value,
            sentinel?.UpdatedAt,
            settings?.PendingChangesSince
        );
    }
}
