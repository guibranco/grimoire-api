using System.Text.Json;
using Grimoire.Api.Controllers;
using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Grimoire.Tests;

public class HealthControllerTests
{
    private static GrimoireDbContext CreateDb() =>
        new(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase($"health-{Guid.NewGuid():N}")
                .Options
        );

    private static JsonElement ToJson(object? value) => JsonSerializer.SerializeToElement(value);

    [Fact]
    public async Task Get_WhenDatabaseIsReachable_ReturnsOk()
    {
        using var db = CreateDb();
        var controller = new HealthController(db);

        var result = await controller.Get(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var body = ToJson(ok.Value);
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.Equal("ok", body.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal(Grimoire.Api.AppVersion.Current, body.GetProperty("version").GetString());
        Assert.True(DateTimeOffset.TryParse(body.GetProperty("time").GetString(), out _));
    }

    [Fact]
    public async Task Get_WhenDatabaseThrows_Returns503()
    {
        var db = CreateDb();
        await db.DisposeAsync();
        var controller = new HealthController(db);

        var result = await controller.Get(CancellationToken.None);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, objectResult.StatusCode);
        var body = ToJson(objectResult.Value);
        Assert.Equal("error", body.GetProperty("status").GetString());
        Assert.Equal("error", body.GetProperty("database").GetProperty("status").GetString());
    }
}
