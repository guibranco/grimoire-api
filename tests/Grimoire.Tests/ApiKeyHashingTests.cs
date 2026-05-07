using Grimoire.Core.Entities;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Grimoire.Tests;

public class ApiKeyHashingTests
{
    private static PasswordHasher<Application> CreateHasher() => new();

    [Fact]
    public void HashAndVerify_Succeeds()
    {
        var hasher = CreateHasher();
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
        };
        var plainKey = "grm_supersecretapikey1234567890ab";

        var hash = hasher.HashPassword(app, plainKey);
        var result = hasher.VerifyHashedPassword(app, hash, plainKey);

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void HashAndVerify_FailsWithWrongKey()
    {
        var hasher = CreateHasher();
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
        };

        var hash = hasher.HashPassword(app, "correct-key");
        var result = hasher.VerifyHashedPassword(app, hash, "wrong-key");

        Assert.Equal(PasswordVerificationResult.Failed, result);
    }

    [Fact]
    public void Hash_IsDifferentEachTime()
    {
        var hasher = CreateHasher();
        var app = new Application
        {
            Id = Guid.NewGuid(),
            Name = "Test",
            Slug = "test",
        };
        var key = "same-api-key";

        var hash1 = hasher.HashPassword(app, key);
        var hash2 = hasher.HashPassword(app, key);

        Assert.NotEqual(hash1, hash2); // salt randomness
    }
}
