using Grimoire.Core.Entities;
using Grimoire.Infrastructure.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Grimoire.Tests;

public class ConsumerActivityTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static GrimoireDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static async Task<(Guid AppId, Guid EnvId)> AddEnvironmentAsync(GrimoireDbContext db)
    {
        var appId = Guid.NewGuid();
        var envId = Guid.NewGuid();
        db.Applications.Add(
            new Application
            {
                Id = appId,
                Name = "app",
                Slug = $"app-{appId:N}",
                ApiKeyHash = "hash",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        db.Environments.Add(
            new AppEnvironment
            {
                Id = envId,
                ApplicationId = appId,
                Name = "Production",
                Slug = "production",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        await db.SaveChangesAsync();
        return (appId, envId);
    }

    [Fact]
    public void Record_MergesReadsPerEnvironment_KeepingLatestClientAndCounting()
    {
        var time = new FakeTimeProvider(Start);
        var tracker = new ConsumerActivityTracker(time);
        var (app, env) = (Guid.NewGuid(), Guid.NewGuid());

        tracker.Record(app, env, "Grimoire.Consumer/1.0", "10.0.0.1");
        time.Advance(TimeSpan.FromMinutes(5));
        tracker.Record(app, env, "grimoire-php/2.0", "10.0.0.2");
        tracker.Record(app, Guid.NewGuid(), "other", null);

        var drained = tracker.Drain();

        Assert.Equal(2, drained.Count);
        var entry = drained.Single(e => e.EnvironmentId == env);
        Assert.Equal(Start.AddMinutes(5), entry.LastSeenAt);
        Assert.Equal("grimoire-php/2.0", entry.LastUserAgent);
        Assert.Equal("10.0.0.2", entry.LastIpAddress);
        Assert.Equal(2, entry.RequestCount);
        Assert.Empty(tracker.Drain());
    }

    [Fact]
    public void Record_TruncatesLongUserAgent_AndBlankBecomesNull()
    {
        var tracker = new ConsumerActivityTracker(new FakeTimeProvider(Start));
        var (app, env) = (Guid.NewGuid(), Guid.NewGuid());

        tracker.Record(app, env, new string('x', 1000), " ");

        var entry = Assert.Single(tracker.Drain());
        Assert.Equal(256, entry.LastUserAgent!.Length);
        Assert.Null(entry.LastIpAddress);
    }

    [Fact]
    public void Requeue_MergesWithReadsRecordedSinceTheDrain()
    {
        var time = new FakeTimeProvider(Start);
        var tracker = new ConsumerActivityTracker(time);
        var (app, env) = (Guid.NewGuid(), Guid.NewGuid());

        tracker.Record(app, env, "old", null);
        var failed = tracker.Drain();
        time.Advance(TimeSpan.FromMinutes(1));
        tracker.Record(app, env, "new", null);
        tracker.Requeue(failed);

        var entry = Assert.Single(tracker.Drain());
        Assert.Equal("new", entry.LastUserAgent);
        Assert.Equal(Start.AddMinutes(1), entry.LastSeenAt);
        Assert.Equal(2, entry.RequestCount);
    }

    [Fact]
    public async Task Store_InsertsThenUpdates_AccumulatingRequestCount()
    {
        await using var db = CreateDb();
        var (app, env) = await AddEnvironmentAsync(db);
        var store = new ConsumerActivityStore(db);

        await store.SaveAsync([new(app, env, Start, "a", "1.1.1.1", 3)], default);
        await store.SaveAsync([new(app, env, Start.AddHours(1), "b", "2.2.2.2", 2)], default);

        var row = await db.ConsumerActivities.SingleAsync();
        Assert.Equal(Start.AddHours(1), row.LastSeenAt);
        Assert.Equal("b", row.LastUserAgent);
        Assert.Equal("2.2.2.2", row.LastIpAddress);
        Assert.Equal(5, row.RequestCount);
    }

    [Fact]
    public async Task Store_OlderEntry_DoesNotMoveLastSeenBackwards()
    {
        await using var db = CreateDb();
        var (app, env) = await AddEnvironmentAsync(db);
        var store = new ConsumerActivityStore(db);

        await store.SaveAsync([new(app, env, Start.AddHours(1), "new", null, 1)], default);
        await store.SaveAsync([new(app, env, Start, "old", null, 1)], default);

        var row = await db.ConsumerActivities.SingleAsync();
        Assert.Equal(Start.AddHours(1), row.LastSeenAt);
        Assert.Equal("new", row.LastUserAgent);
        Assert.Equal(2, row.RequestCount);
    }

    [Fact]
    public async Task Store_SkipsEnvironmentsThatNoLongerExist()
    {
        await using var db = CreateDb();
        var (app, _) = await AddEnvironmentAsync(db);
        var store = new ConsumerActivityStore(db);

        await store.SaveAsync([new(app, Guid.NewGuid(), Start, null, null, 1)], default);

        Assert.Empty(db.ConsumerActivities);
    }

    [Fact]
    public async Task Reader_OverlaysUnflushedReadsOnStoredOnes()
    {
        await using var db = CreateDb();
        var (app, env) = await AddEnvironmentAsync(db);
        var (_, quietEnv) = await AddEnvironmentAsync(db);
        var time = new FakeTimeProvider(Start.AddDays(1));
        var tracker = new ConsumerActivityTracker(time);
        await new ConsumerActivityStore(db).SaveAsync(
            [new(app, env, Start, null, null, 1)],
            default
        );
        tracker.Record(app, env, null, null);

        var lastSeen = await new ConsumerActivityReader(db, tracker).GetLastSeenAsync();

        Assert.NotNull(lastSeen);
        Assert.Equal(Start.AddDays(1), lastSeen[(app, env)]);
        Assert.DoesNotContain(lastSeen.Keys, k => k.EnvironmentId == quietEnv);
    }

    [Fact]
    public async Task Reader_ReturnsEmptyMap_NotNull_SoConsumerRulesApply()
    {
        await using var db = CreateDb();
        var reader = new ConsumerActivityReader(
            db,
            new ConsumerActivityTracker(new FakeTimeProvider(Start))
        );

        var lastSeen = await reader.GetLastSeenAsync();

        Assert.NotNull(lastSeen);
        Assert.Empty(lastSeen);
    }
}
