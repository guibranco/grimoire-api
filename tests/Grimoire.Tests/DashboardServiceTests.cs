using Grimoire.Core.Dashboard;
using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence;
using Grimoire.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace Grimoire.Tests;

public class DashboardServiceTests
{
    private static GrimoireDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );

    private static DashboardService CreateService(GrimoireDbContext db, int cacheSeconds) =>
        new(
            db,
            new UntrackedConsumerActivityReader(),
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DashboardOptions { CacheSeconds = cacheSeconds }),
            TimeProvider.System
        );

    private static async Task AddApplicationAsync(GrimoireDbContext db, bool deleted = false)
    {
        var name = $"app-{Guid.NewGuid():N}";
        db.Applications.Add(
            new Application
            {
                Id = Guid.NewGuid(),
                Name = name,
                Slug = name,
                ApiKeyHash = "hash",
                IsDeleted = deleted,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task CachesSummary_ForConfiguredDuration()
    {
        await using var db = CreateDb();
        var service = CreateService(db, cacheSeconds: 60);
        await AddApplicationAsync(db);

        var first = await service.GetSummaryAsync();
        await AddApplicationAsync(db);
        var second = await service.GetSummaryAsync();

        Assert.Same(first, second);
        Assert.Equal(1, second.Totals.Applications);
    }

    [Fact]
    public async Task CacheIsKeyedByThresholds()
    {
        await using var db = CreateDb();
        var service = CreateService(db, cacheSeconds: 60);

        var first = await service.GetSummaryAsync(expiringWithinDays: 14);
        var other = await service.GetSummaryAsync(expiringWithinDays: 30);

        Assert.NotSame(first, other);
    }

    [Fact]
    public async Task ZeroCacheSeconds_AlwaysRebuilds_AndExcludesDeletedApplications()
    {
        await using var db = CreateDb();
        var service = CreateService(db, cacheSeconds: 0);
        await AddApplicationAsync(db);
        await AddApplicationAsync(db, deleted: true);

        var first = await service.GetSummaryAsync();
        await AddApplicationAsync(db);
        var second = await service.GetSummaryAsync();

        Assert.Equal(1, first.Totals.Applications);
        Assert.Equal(2, second.Totals.Applications);
    }
}
