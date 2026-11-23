using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Services;
using Grimoire.Infrastructure.Persistence;
using Grimoire.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Grimoire.Tests;

public class ConfigurationPrecedenceTests
{
    private static readonly Guid AppId = Guid.NewGuid();

    private static ConfigurationEntry App(string key, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            ApplicationId = AppId,
            Key = key,
            Value = value,
        };

    private static GlobalConfigurationEntry Global(string slug, string key, string value) =>
        new()
        {
            Id = Guid.NewGuid(),
            EnvironmentSlug = slug,
            Key = key,
            Value = value,
        };

    private static EffectiveConfigurationEntry Single(
        IReadOnlyList<EffectiveConfigurationEntry> entries,
        string key
    ) => Assert.Single(entries, e => e.Key == key);

    [Fact]
    public void StarGlobal_AppliesToAnyEnvironment()
    {
        var result = ConfigurationPrecedence.Merge(
            [],
            [Global("*", "LogStream:Url", "https://logs")],
            "production",
            inheritGlobals: true
        );

        var entry = Single(result, "LogStream:Url");
        Assert.Equal("https://logs", entry.Value);
        Assert.Equal(ValueSource.Global, entry.Source);
        Assert.Equal("*", entry.GlobalEnvironmentSlug);
        Assert.False(entry.OverridesGlobal);
    }

    [Fact]
    public void SlugGlobal_BeatsStarGlobal()
    {
        var result = ConfigurationPrecedence.Merge(
            [],
            [Global("*", "K", "star"), Global("production", "K", "prod")],
            "production",
            inheritGlobals: true
        );

        var entry = Single(result, "K");
        Assert.Equal("prod", entry.Value);
        Assert.Equal("production", entry.GlobalEnvironmentSlug);
    }

    [Fact]
    public void SlugGlobal_DoesNotApplyToOtherEnvironments()
    {
        var result = ConfigurationPrecedence.Merge(
            [],
            [
                Global("*", "K", "star"),
                Global("production", "K", "prod"),
                Global("production", "Only", "x"),
            ],
            "staging",
            inheritGlobals: true
        );

        Assert.Equal("star", Single(result, "K").Value);
        Assert.DoesNotContain(result, e => e.Key == "Only");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("production")]
    public void AppValue_BeatsGlobal_AndReportsOverride(string globalSlug)
    {
        var result = ConfigurationPrecedence.Merge(
            [App("K", "app")],
            [Global(globalSlug, "K", "global")],
            "production",
            inheritGlobals: true
        );

        var entry = Single(result, "K");
        Assert.Equal("app", entry.Value);
        Assert.Equal(ValueSource.Application, entry.Source);
        Assert.True(entry.OverridesGlobal);
        Assert.Equal(globalSlug, entry.GlobalEnvironmentSlug);
    }

    [Fact]
    public void AppValue_WithoutMatchingGlobal_IsNotAnOverride()
    {
        var result = ConfigurationPrecedence.Merge(
            [App("K", "app")],
            [Global("*", "Other", "g")],
            "local",
            inheritGlobals: true
        );

        var entry = Single(result, "K");
        Assert.False(entry.OverridesGlobal);
        Assert.Null(entry.GlobalEnvironmentSlug);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void InheritGlobalsFalse_ReturnsOnlyApplicationValues()
    {
        var result = ConfigurationPrecedence.Merge(
            [App("K", "app")],
            [Global("*", "K", "g"), Global("*", "G", "g")],
            "local",
            inheritGlobals: false
        );

        var entry = Assert.Single(result);
        Assert.Equal("K", entry.Key);
        Assert.False(entry.OverridesGlobal);
    }

    [Fact]
    public void AppValue_OverridesGlobal_KeyComparedCaseInsensitively()
    {
        var result = ConfigurationPrecedence.Merge(
            [App("Logstream:url", "app")],
            [Global("*", "LogStream:Url", "global")],
            "local",
            inheritGlobals: true
        );

        var entry = Assert.Single(result);
        Assert.Equal("app", entry.Value);
        Assert.True(entry.OverridesGlobal);
    }
}

public class EffectiveConfigurationServiceTests
{
    private readonly GrimoireDbContext _db;
    private readonly EffectiveConfigurationService _service;
    private readonly Application _app;
    private readonly AppEnvironment _prod;
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    public EffectiveConfigurationServiceTests()
    {
        _db = new GrimoireDbContext(
            new DbContextOptionsBuilder<GrimoireDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options
        );
        _service = new EffectiveConfigurationService(
            new ConfigurationRepository(_db),
            new GlobalConfigurationRepository(_db),
            new SecretRepository(_db),
            new GlobalSecretRepository(_db)
        );

        _app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "App",
            Slug = "app",
            ApiKeyHash = "hash",
        };
        _prod = new AppEnvironment
        {
            Id = Guid.NewGuid(),
            ApplicationId = _app.Id,
            Name = "Production",
            Slug = "production",
        };
        _db.Applications.Add(_app);
        _db.Environments.Add(_prod);
        _db.SaveChanges();
    }

    private Secret AddAppSecret(string name, params SecretVersion[] versions)
    {
        var secret = new Secret
        {
            Id = Guid.NewGuid(),
            ApplicationId = _app.Id,
            Name = name,
        };
        _db.Secrets.Add(secret);
        foreach (var v in versions)
        {
            v.Id = Guid.NewGuid();
            v.SecretId = secret.Id;
            v.EnvironmentId = _prod.Id;
            _db.SecretVersions.Add(v);
        }
        _db.SaveChanges();
        return secret;
    }

    private GlobalSecret AddGlobalSecret(string name, params GlobalSecretVersion[] versions)
    {
        var secret = new GlobalSecret { Id = Guid.NewGuid(), Name = name };
        _db.GlobalSecrets.Add(secret);
        foreach (var v in versions)
        {
            v.Id = Guid.NewGuid();
            v.GlobalSecretId = secret.Id;
            _db.GlobalSecretVersions.Add(v);
        }
        _db.SaveChanges();
        return secret;
    }

    private static GlobalSecretVersion GV(
        string slug,
        string value,
        int version = 1,
        bool enabled = true
    ) =>
        new()
        {
            EnvironmentSlug = slug,
            EncryptedValue = value,
            Version = version,
            IsEnabled = enabled,
        };

    [Fact]
    public async Task ResolveSecret_FallsBackToStarGlobal_WhenAppHasNoSecret()
    {
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.Resolved, result.Status);
        Assert.Equal("enc-star", result.Value!.EncryptedValue);
        Assert.Equal(ValueSource.Global, result.Value.Source);
    }

    [Fact]
    public async Task ResolveSecret_SlugGlobalVersion_BeatsStar()
    {
        AddGlobalSecret("amqp", GV("*", "enc-star", version: 5), GV("production", "enc-prod"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal("enc-prod", result.Value!.EncryptedValue);
    }

    [Fact]
    public async Task ResolveSecret_SlugGlobalInactive_FallsBackToStar()
    {
        AddGlobalSecret("amqp", GV("*", "enc-star"), GV("production", "enc-prod", enabled: false));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal("enc-star", result.Value!.EncryptedValue);
    }

    [Fact]
    public async Task ResolveSecret_SlugGlobalForOtherEnvironment_IsIgnored()
    {
        AddGlobalSecret("amqp", GV("staging", "enc-staging"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.NoActiveVersion, result.Status);
    }

    [Fact]
    public async Task ResolveSecret_AppSecretWins_OverGlobal()
    {
        AddAppSecret(
            "amqp",
            new SecretVersion
            {
                EncryptedValue = "enc-app",
                IsEnabled = true,
                Version = 1,
            }
        );
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal("enc-app", result.Value!.EncryptedValue);
        Assert.Equal(ValueSource.Application, result.Value.Source);
    }

    [Fact]
    public async Task ResolveSecret_AppSecretDisabled_DoesNotFallBackToGlobal()
    {
        AddAppSecret(
            "amqp",
            new SecretVersion
            {
                EncryptedValue = "enc-app",
                IsEnabled = false,
                Version = 1,
            }
        );
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.NoActiveVersion, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ResolveSecret_AppSecretExpired_DoesNotFallBackToGlobal()
    {
        AddAppSecret(
            "amqp",
            new SecretVersion
            {
                EncryptedValue = "enc-app",
                IsEnabled = true,
                ExpiresAt = Now.AddDays(-1),
                Version = 1,
            }
        );
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.NoActiveVersion, result.Status);
    }

    [Fact]
    public async Task ResolveSecret_AppSecretWithoutVersions_DoesNotFallBackToGlobal()
    {
        AddAppSecret("amqp");
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.NoActiveVersion, result.Status);
    }

    [Fact]
    public async Task ResolveSecret_InheritGlobalsFalse_ReturnsNotFound()
    {
        _app.InheritGlobals = false;
        AddGlobalSecret("amqp", GV("*", "enc-star"));

        var result = await _service.ResolveSecretAsync(_app, _prod, "amqp", Now);

        Assert.Equal(SecretResolutionStatus.SecretNotFound, result.Status);
    }

    [Fact]
    public async Task ResolveSecret_NoAppOrGlobalSecret_ReturnsNotFound()
    {
        var result = await _service.ResolveSecretAsync(_app, _prod, "missing", Now);

        Assert.Equal(SecretResolutionStatus.SecretNotFound, result.Status);
    }

    [Fact]
    public async Task GetSecrets_MergesAppAndGlobal_WithOverrideFlags()
    {
        AddAppSecret("shared");
        AddAppSecret("app-only");
        AddGlobalSecret("shared");
        AddGlobalSecret("global-only");

        var result = await _service.GetSecretsAsync(_app);

        Assert.Equal<string>(["app-only", "global-only", "shared"], result.Select(s => s.Name));
        var shared = result.Single(s => s.Name == "shared");
        Assert.Equal(ValueSource.Application, shared.Source);
        Assert.True(shared.OverridesGlobal);
        Assert.Equal(ValueSource.Global, result.Single(s => s.Name == "global-only").Source);
        Assert.False(result.Single(s => s.Name == "app-only").OverridesGlobal);
    }

    [Fact]
    public async Task GetSecrets_InheritGlobalsFalse_ReturnsOnlyAppSecrets()
    {
        _app.InheritGlobals = false;
        AddAppSecret("shared");
        AddGlobalSecret("shared");
        AddGlobalSecret("global-only");

        var result = await _service.GetSecretsAsync(_app);

        var only = Assert.Single(result);
        Assert.Equal("shared", only.Name);
        Assert.False(only.OverridesGlobal);
    }

    [Fact]
    public async Task GetConfigurationValue_FallsBackThroughPrecedence()
    {
        _db.ConfigurationEntries.Add(
            new ConfigurationEntry
            {
                Id = Guid.NewGuid(),
                ApplicationId = _app.Id,
                EnvironmentId = _prod.Id,
                Key = "AppKey",
                Value = "app",
            }
        );
        _db.GlobalConfigurationEntries.AddRange(
            new GlobalConfigurationEntry
            {
                Id = Guid.NewGuid(),
                EnvironmentSlug = "*",
                Key = "AppKey",
                Value = "star",
            },
            new GlobalConfigurationEntry
            {
                Id = Guid.NewGuid(),
                EnvironmentSlug = "*",
                Key = "Shared",
                Value = "star",
            },
            new GlobalConfigurationEntry
            {
                Id = Guid.NewGuid(),
                EnvironmentSlug = "production",
                Key = "Shared",
                Value = "prod",
            }
        );
        await _db.SaveChangesAsync();

        Assert.Equal(
            "app",
            (await _service.GetConfigurationValueAsync(_app, _prod, "AppKey"))!.Value
        );
        Assert.Equal(
            "prod",
            (await _service.GetConfigurationValueAsync(_app, _prod, "Shared"))!.Value
        );
        Assert.Null(await _service.GetConfigurationValueAsync(_app, _prod, "Missing"));

        _app.InheritGlobals = false;
        Assert.Null(await _service.GetConfigurationValueAsync(_app, _prod, "Shared"));
    }
}
