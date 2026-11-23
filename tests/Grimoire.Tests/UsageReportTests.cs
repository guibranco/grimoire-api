using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Reports;
using Grimoire.Infrastructure.ConsumerTracking;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Grimoire.Tests;

public class UsageReportTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    private static GrimoireDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    // ---- tracker ----

    [Fact]
    public void Tracker_MergesReadsPerEnvironmentAndSecret()
    {
        var time = new FakeTimeProvider(Start);
        var tracker = new SecretReadTracker(time);
        var (app, env, secret) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        tracker.Record(app, env, ValueSource.Application, secret);
        time.Advance(TimeSpan.FromMinutes(5));
        tracker.Record(app, env, ValueSource.Application, secret);
        tracker.Record(app, env, ValueSource.Global, secret);

        var drained = tracker.Drain();
        Assert.Equal(2, drained.Count);
        var appRead = Assert.Single(drained, d => d.Source == ValueSource.Application);
        Assert.Equal((Start.AddMinutes(5), 2L), (appRead.LastReadAt, appRead.ReadCount));
    }

    [Fact]
    public void Tracker_IgnoresAnUnknownSecret()
    {
        var tracker = new SecretReadTracker(new FakeTimeProvider(Start));
        tracker.Record(Guid.NewGuid(), Guid.NewGuid(), ValueSource.Application, Guid.Empty);
        Assert.Empty(tracker.Drain());
    }

    [Fact]
    public void Tracker_PendingIncludesInFlightUntilCompleted_AndRequeueRestores()
    {
        var time = new FakeTimeProvider(Start);
        var tracker = new SecretReadTracker(time);
        var (app, env, secret) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        tracker.Record(app, env, ValueSource.Application, secret);

        var inFlight = tracker.Drain();
        time.Advance(TimeSpan.FromMinutes(1));
        tracker.Record(app, env, ValueSource.Application, secret);

        var pending = Assert.Single(tracker.Pending());
        Assert.Equal((Start.AddMinutes(1), 2L), (pending.LastReadAt, pending.ReadCount));

        tracker.Requeue(inFlight);
        var again = Assert.Single(tracker.Drain());
        Assert.Equal(2, again.ReadCount);
        tracker.Complete([again]);
        Assert.Empty(tracker.Pending());
    }

    // ---- store ----

    private static async Task<(Guid App, Guid Env, Guid Secret, Guid Global)> SeedAsync(
        GrimoireDbContext db
    )
    {
        var app = Guid.NewGuid();
        var env = Guid.NewGuid();
        var secret = Guid.NewGuid();
        var global = Guid.NewGuid();
        db.Applications.Add(
            new Application
            {
                Id = app,
                Name = "App",
                Slug = "app",
                ApiKeyHash = "x",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        db.Environments.Add(
            new AppEnvironment
            {
                Id = env,
                ApplicationId = app,
                Name = "Prod",
                Slug = "prod",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        db.Secrets.Add(
            new Secret
            {
                Id = secret,
                ApplicationId = app,
                Name = "Db",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        db.GlobalSecrets.Add(
            new GlobalSecret
            {
                Id = global,
                Name = "Smtp",
                CreatedAt = Start,
                UpdatedAt = Start,
            }
        );
        await db.SaveChangesAsync();
        return (app, env, secret, global);
    }

    [Fact]
    public async Task Store_InsertsThenAccumulates_NeverMovingLastReadBackwards()
    {
        await using var db = CreateDb();
        var (app, env, secret, global) = await SeedAsync(db);
        var store = new SecretReadStore(db);

        await store.SaveAsync(
            [
                new(app, env, ValueSource.Application, secret, Start.AddHours(2), 3),
                new(app, env, ValueSource.Global, global, Start, 1),
            ],
            default
        );
        await store.SaveAsync(
            [new(app, env, ValueSource.Application, secret, Start.AddHours(1), 2)],
            default
        );

        var appRow = await db.SecretReads.SingleAsync(r => r.SecretId == secret);
        Assert.Equal((Start.AddHours(2), 5L), (appRow.LastReadAt, appRow.ReadCount));
        var globalRow = await db.SecretReads.SingleAsync(r => r.GlobalSecretId == global);
        Assert.Null(globalRow.SecretId);
    }

    [Fact]
    public async Task Store_SkipsDeletedEnvironmentsAndSecrets()
    {
        await using var db = CreateDb();
        var (app, env, secret, _) = await SeedAsync(db);

        await new SecretReadStore(db).SaveAsync(
            [
                new(app, Guid.NewGuid(), ValueSource.Application, secret, Start, 1),
                new(app, env, ValueSource.Application, Guid.NewGuid(), Start, 1),
                new(app, env, ValueSource.Global, Guid.NewGuid(), Start, 1),
            ],
            default
        );

        Assert.Empty(db.SecretReads);
    }

    [Fact]
    public async Task Store_NothingToSave_IsANoOp()
    {
        await using var db = CreateDb();
        await new SecretReadStore(db).SaveAsync([], default);
        Assert.Empty(db.SecretReads);
    }

    // ---- unused secrets ----

    private static SecretUsageSubject Subject(string name, string? app = "app") =>
        new(Guid.NewGuid(), app is null, app, app is null ? null : "App", name, Start);

    private static SecretReadSummary ReadOf(
        SecretUsageSubject s,
        string env,
        DateTimeOffset at,
        long count = 1
    ) => new(s.Id, s.IsGlobal, "app", env, at, count);

    [Fact]
    public void Unused_ListsNeverReadThenOldest_AndSkipsRecentlyRead()
    {
        var never = Subject("never");
        var stale = Subject("stale");
        var fresh = Subject("fresh");
        var global = Subject("global", app: null);

        var result = UnusedSecretsBuilder.Build(
            [fresh, stale, never, global],
            [
                ReadOf(stale, "dev", Start.AddDays(1), 2),
                ReadOf(stale, "prod", Start.AddDays(2), 3),
                ReadOf(fresh, "prod", Start.AddDays(20)),
            ],
            since: Start.AddDays(10)
        );

        Assert.Equal(["never", "global", "stale"], result.Select(r => r.Secret.Name));
        Assert.Null(result[0].LastReadAt);
        var staleResult = result[2];
        Assert.Equal((Start.AddDays(2), 5L), (staleResult.LastReadAt, staleResult.ReadCount));
        Assert.Equal(["prod", "dev"], staleResult.Reads.Select(r => r.Environment));
    }

    [Fact]
    public void Unused_DoesNotMixUpAnApplicationAndAGlobalSecretWithTheSameId()
    {
        var appSecret = Subject("x");
        var global = appSecret with { IsGlobal = true, ApplicationSlug = null };

        var result = UnusedSecretsBuilder.Build(
            [appSecret, global],
            [new(appSecret.Id, true, "app", "prod", Start.AddDays(20), 1)],
            since: Start.AddDays(10)
        );

        Assert.False(Assert.Single(result).Secret.IsGlobal);
    }

    // ---- dependency graph ----

    [Fact]
    public void Graph_LinksOwnershipOverridesInheritanceAndReads()
    {
        var inheriting = new GraphApplication(Guid.NewGuid(), "web", "Web", true);
        var isolated = new GraphApplication(Guid.NewGuid(), "batch", "Batch", false);
        var webDb = new GraphSecret(Guid.NewGuid(), inheriting.Id, "Db");
        var batchDb = new GraphSecret(Guid.NewGuid(), isolated.Id, "Db");
        var globalDb = new GraphSecret(Guid.NewGuid(), null, "Db");
        var orphan = new GraphSecret(Guid.NewGuid(), Guid.NewGuid(), "Orphan");

        var graph = DependencyGraphBuilder.Build(
            new DependencyGraphInput(
                [inheriting, isolated],
                [webDb, batchDb, globalDb, orphan],
                [
                    new(inheriting.Id, "Timeout", "dev"),
                    new(inheriting.Id, "Timeout", "prod"),
                    new(isolated.Id, "Timeout", "prod"),
                    new(null, "Timeout", "*"),
                    new(null, "Region", "*"),
                    new(null, "Region", "prod"),
                    new(Guid.NewGuid(), "Ghost", "prod"),
                ],
                [
                    new(webDb.Id, false, "web", "prod", Start, 2),
                    new(webDb.Id, false, "web", "dev", Start.AddDays(1), 1),
                    new(globalDb.Id, true, "batch", "prod", Start, 1),
                    new(Guid.NewGuid(), false, "web", "prod", Start, 1),
                    new(globalDb.Id, true, "deleted-app", "prod", Start, 1),
                ]
            )
        );

        var ids = graph.Nodes.Select(n => n.Id).ToList();
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
        Assert.DoesNotContain(ids, id => id.Contains("Orphan") || id.Contains("Ghost"));
        var region = graph.Nodes.Single(n => n.Id == "global-config:Region");
        Assert.Equal(["*", "prod"], region.Environments);
        Assert.Equal(
            ["dev", "prod"],
            graph.Nodes.Single(n => n.Id == "config:web/Timeout").Environments
        );

        var edges = graph.Edges.Select(e => (e.Source, e.Target, e.Kind)).ToHashSet();
        Assert.Contains(("app:web", "secret:web/Db", "owns"), edges);
        Assert.Contains(("secret:web/Db", "global-secret:Db", "overrides"), edges);
        Assert.DoesNotContain(("secret:batch/Db", "global-secret:Db", "overrides"), edges);
        Assert.Contains(("config:web/Timeout", "global-config:Timeout", "overrides"), edges);
        Assert.Contains(("app:web", "global-config:Region", "inherits"), edges);
        Assert.DoesNotContain(("app:web", "global-config:Timeout", "inherits"), edges);
        Assert.DoesNotContain(edges, e => e.Source == "app:batch" && e.Kind == "inherits");
        Assert.Contains(("app:batch", "global-secret:Db", "reads"), edges);

        var read = graph.Edges.Single(e => e.Kind == "reads" && e.Source == "app:web");
        Assert.Equal("secret:web/Db", read.Target);
        Assert.Equal(["dev", "prod"], read.Environments);
        Assert.Equal((Start.AddDays(1), 3L), (read.LastReadAt, read.ReadCount));
        Assert.Equal(2, graph.Edges.Count(e => e.Kind == "reads"));
    }
}
