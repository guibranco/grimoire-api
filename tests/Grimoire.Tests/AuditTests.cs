using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Audit;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Grimoire.Tests;

public class AuditTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 22, 10, 0, 0, TimeSpan.Zero);

    private static AuditEvent Event(
        string action,
        DateTimeOffset at,
        string? app = "app",
        string? env = null,
        string actor = "admin"
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            OccurredAt = at,
            Actor = actor,
            Action = action,
            ApplicationSlug = app,
            Environment = env,
            Outcome = AuditOutcome.Success,
            StatusCode = 200,
        };

    [Fact]
    public void EveryAction_HasALabelAndTheReadsAreRecognised()
    {
        foreach (var action in AuditActions.All)
            Assert.NotEqual(action, AuditActions.Label(action));
        Assert.True(AuditActions.IsRead(AuditActions.SecretRead));
        Assert.True(AuditActions.IsRead(AuditActions.ConfigurationRead));
        Assert.False(AuditActions.IsRead(AuditActions.SecretWrite));
        Assert.Equal("unknown.code", AuditActions.Label("unknown.code"));
    }

    [Theory]
    [InlineData(200, AuditOutcome.Success)]
    [InlineData(201, AuditOutcome.Success)]
    [InlineData(204, AuditOutcome.Success)]
    [InlineData(400, AuditOutcome.Invalid)]
    [InlineData(401, AuditOutcome.Forbidden)]
    [InlineData(403, AuditOutcome.Forbidden)]
    [InlineData(404, AuditOutcome.NotFound)]
    [InlineData(409, AuditOutcome.Conflict)]
    [InlineData(500, AuditOutcome.Error)]
    public void OutcomeFor_MapsStatusCodes(int status, AuditOutcome expected) =>
        Assert.Equal(expected, AuditActions.OutcomeFor(status));

    [Fact]
    public void AuditLog_NormalisesAndTruncates()
    {
        var log = new AuditLog(new AuditOptions());
        var e = Event(
            AuditActions.SecretRead,
            new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(2))
        );
        e.Id = Guid.Empty;
        e.UserAgent = new string('u', 1000);
        e.Target = "  ";

        log.Record(e);

        Assert.True(log.Reader.TryRead(out var queued));
        Assert.NotEqual(Guid.Empty, queued!.Id);
        Assert.Equal(TimeSpan.Zero, queued.OccurredAt.Offset);
        Assert.Equal(256, queued.UserAgent!.Length);
        Assert.Null(queued.Target);
    }

    [Fact]
    public void AuditLog_WhenFull_DropsAndCounts()
    {
        var log = new AuditLog(new AuditOptions { QueueCapacity = 2 });
        for (var i = 0; i < 5; i++)
            log.Record(Event(AuditActions.SecretRead, Start));

        Assert.Equal(3, log.TakeDroppedCount());
        Assert.Equal(0, log.TakeDroppedCount());
    }

    [Fact]
    public void AuditLog_WhenDisabled_RecordsNothing()
    {
        var log = new AuditLog(new AuditOptions { Enabled = false });
        log.Record(Event(AuditActions.SecretRead, Start));
        Assert.False(log.Reader.TryRead(out _));
    }

    private static async Task<GrimoireDbContext> SeededDbAsync()
    {
        var db = new GrimoireDbContext(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        db.AuditEvents.AddRange(
            Event(AuditActions.SecretCreate, Start, env: null),
            Event(AuditActions.SecretWrite, Start.AddMinutes(1), env: "local,production"),
            Event(
                AuditActions.SecretRead,
                Start.AddMinutes(2),
                env: "production",
                actor: "app:app"
            ),
            Event(
                AuditActions.ConfigurationUpdate,
                Start.AddMinutes(3),
                app: "other",
                env: "staging"
            )
        );
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Query_FiltersByActionPrefix_AndExcludesReads()
    {
        await using var db = await SeededDbAsync();
        var service = new AuditQueryService(db);

        var secrets = await service.QueryAsync(new AuditQuery(Action: "secret.*"));
        var changes = await service.QueryAsync(
            new AuditQuery(Action: "secret.*", IncludeReads: false)
        );

        Assert.Equal(3, secrets.TotalCount);
        Assert.Equal(2, changes.TotalCount);
        Assert.DoesNotContain(changes.Items, e => e.Action == AuditActions.SecretRead);
    }

    [Fact]
    public async Task Query_MatchesOneEnvironmentOfAMultiEnvironmentEvent()
    {
        await using var db = await SeededDbAsync();
        var page = await new AuditQueryService(db).QueryAsync(new AuditQuery(Environment: "local"));

        var only = Assert.Single(page.Items);
        Assert.Equal(AuditActions.SecretWrite, only.Action);
    }

    [Fact]
    public async Task Query_OrdersNewestFirst_PagesAndFiltersByTime()
    {
        await using var db = await SeededDbAsync();
        var service = new AuditQueryService(db);

        var first = await service.QueryAsync(new AuditQuery(PageSize: 2));
        var second = await service.QueryAsync(new AuditQuery(Page: 2, PageSize: 2));
        var window = await service.QueryAsync(
            new AuditQuery(From: Start.AddMinutes(1), To: Start.AddMinutes(2))
        );

        Assert.Equal(4, first.TotalCount);
        Assert.Equal(AuditActions.ConfigurationUpdate, first.Items[0].Action);
        Assert.Equal(AuditActions.SecretCreate, second.Items[^1].Action);
        Assert.Equal(2, window.TotalCount);
    }

    [Fact]
    public async Task Query_FiltersByApplicationAndActor()
    {
        await using var db = await SeededDbAsync();
        var service = new AuditQueryService(db);

        Assert.Equal(
            1,
            (await service.QueryAsync(new AuditQuery(Application: "other"))).TotalCount
        );
        Assert.Equal(1, (await service.QueryAsync(new AuditQuery(Actor: "app:app"))).TotalCount);
    }
}
