using Grimoire.Core.Entities;
using Grimoire.Infrastructure.Persistence;
using Grimoire.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Grimoire.Tests;

public class SecretResolutionTests
{
    private static GrimoireDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<GrimoireDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new GrimoireDbContext(options);
    }

    private static (Guid secretId, Guid envId) SeedBasicData(GrimoireDbContext db)
    {
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
            ApiKeyHash = "hash",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var env = new AppEnvironment
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = "Local",
            Slug = "local",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            ApplicationId = app.Id,
            Name = "db-pass",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
        db.Applications.Add(app);
        db.Environments.Add(env);
        db.Secrets.Add(secret);
        db.SaveChanges();
        return (secret.Id, env.Id);
    }

    [Fact]
    public async Task GetActiveVersion_ReturnsEnabledVersion()
    {
        var db = CreateDb();
        var (secretId, envId) = SeedBasicData(db);
        db.SecretVersions.Add(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc",
                IsEnabled = true,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var repo = new SecretRepository(db);
        var version = await repo.GetActiveVersionAsync(secretId, envId, DateTimeOffset.UtcNow);

        Assert.NotNull(version);
        Assert.Equal(1, version.Version);
    }

    [Fact]
    public async Task GetActiveVersion_ReturnsNull_WhenDisabled()
    {
        var db = CreateDb();
        var (secretId, envId) = SeedBasicData(db);
        db.SecretVersions.Add(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc",
                IsEnabled = false,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var repo = new SecretRepository(db);
        var version = await repo.GetActiveVersionAsync(secretId, envId, DateTimeOffset.UtcNow);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetActiveVersion_ReturnsNull_WhenExpired()
    {
        var db = CreateDb();
        var (secretId, envId) = SeedBasicData(db);
        db.SecretVersions.Add(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc",
                IsEnabled = true,
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1),
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var repo = new SecretRepository(db);
        var version = await repo.GetActiveVersionAsync(secretId, envId, DateTimeOffset.UtcNow);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetActiveVersion_ReturnsNull_WhenNotBeforeInFuture()
    {
        var db = CreateDb();
        var (secretId, envId) = SeedBasicData(db);
        db.SecretVersions.Add(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc",
                IsEnabled = true,
                NotBefore = DateTimeOffset.UtcNow.AddDays(1),
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var repo = new SecretRepository(db);
        var version = await repo.GetActiveVersionAsync(secretId, envId, DateTimeOffset.UtcNow);

        Assert.Null(version);
    }

    [Fact]
    public async Task GetActiveVersion_ReturnsLatestVersion()
    {
        var db = CreateDb();
        var (secretId, envId) = SeedBasicData(db);
        db.SecretVersions.AddRange(
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc1",
                IsEnabled = true,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow,
            },
            new SecretVersion
            {
                Id = Guid.NewGuid(),
                SecretId = secretId,
                EnvironmentId = envId,
                EncryptedValue = "enc2",
                IsEnabled = true,
                Version = 2,
                CreatedAt = DateTimeOffset.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        var repo = new SecretRepository(db);
        var version = await repo.GetActiveVersionAsync(secretId, envId, DateTimeOffset.UtcNow);

        Assert.Equal(2, version!.Version);
    }
}
