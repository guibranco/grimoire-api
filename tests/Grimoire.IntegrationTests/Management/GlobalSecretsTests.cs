using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Grimoire.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests.Management;

public class GlobalSecretsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    [Fact]
    public async Task Create_Returns201_AndGetByNameReturnsMetadata()
    {
        var name = TestHelpers.UniqueName("amqp");

        var response = await Client.PostAsJsonAsync(
            "/management/global/secrets",
            new { name, description = "CloudAMQP" }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var get = await Client.GetAsync($"/management/global/secrets/{name}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var json = JsonNode.Parse(await get.Content.ReadAsStringAsync())!;
        Assert.Equal(name, json["name"]!.GetValue<string>());
        Assert.Equal("CloudAMQP", json["description"]!.GetValue<string>());
    }

    [Fact]
    public async Task Create_Duplicate_Returns409()
    {
        var name = TestHelpers.UniqueName("dup");
        await TestHelpers.CreateGlobalSecretAsync(Client, name);

        var response = await Client.PostAsJsonAsync("/management/global/secrets", new { name });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetValues_StoresEncrypted_AndListAndVersionsNeverReturnValues()
    {
        var name = TestHelpers.UniqueName("enc");
        const string plain = "amqps://user:p4ss@host/vhost";
        await TestHelpers.CreateGlobalSecretAsync(
            Client,
            name,
            ("*", plain, true),
            ("production", plain + "-prod", true)
        );

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
            var stored = await db
                .GlobalSecretVersions.Where(v => v.GlobalSecret.Name == name)
                .ToListAsync();
            Assert.Equal(2, stored.Count);
            Assert.All(stored, v => Assert.DoesNotContain("p4ss", v.EncryptedValue));
        }

        var listBody = await Client.GetStringAsync("/management/global/secrets");
        Assert.DoesNotContain("p4ss", listBody);

        var versions = await Client.GetAsync($"/management/global/secrets/{name}/versions/*");
        Assert.Equal(HttpStatusCode.OK, versions.StatusCode);
        var versionsBody = await versions.Content.ReadAsStringAsync();
        Assert.DoesNotContain("p4ss", versionsBody);
        var arr = JsonNode.Parse(versionsBody)!.AsArray();
        Assert.Single(arr);
        Assert.Equal(1, arr[0]!["version"]!.GetValue<int>());
        Assert.Null(arr[0]!["value"]);
    }

    [Fact]
    public async Task SetValues_IncrementsVersionPerEnvironment()
    {
        var name = TestHelpers.UniqueName("ver");
        await TestHelpers.CreateGlobalSecretAsync(Client, name, ("production", "v1", true));
        await Client.PostAsJsonAsync(
            $"/management/global/secrets/{name}/values",
            new[]
            {
                new
                {
                    environmentSlug = "production",
                    value = "v2",
                    isEnabled = true,
                },
                new
                {
                    environmentSlug = "*",
                    value = "star",
                    isEnabled = true,
                },
            }
        );

        var prod = JsonNode
            .Parse(
                await Client.GetStringAsync(
                    $"/management/global/secrets/{name}/versions/production"
                )
            )!
            .AsArray();
        Assert.Equal<int>([2, 1], prod.Select(v => v!["version"]!.GetValue<int>()));

        var star = JsonNode
            .Parse(await Client.GetStringAsync($"/management/global/secrets/{name}/versions/*"))!
            .AsArray();
        Assert.Single(star);
    }

    [Fact]
    public async Task SetValues_InvalidEnvironmentSlug_Returns400()
    {
        var name = TestHelpers.UniqueName("bad-env");
        await TestHelpers.CreateGlobalSecretAsync(Client, name);

        var response = await Client.PostAsJsonAsync(
            $"/management/global/secrets/{name}/values",
            new[]
            {
                new
                {
                    environmentSlug = "Not Valid",
                    value = "x",
                    isEnabled = true,
                },
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SetValues_UnknownSecret_Returns404()
    {
        var response = await Client.PostAsJsonAsync(
            "/management/global/secrets/no-such-secret/values",
            new[]
            {
                new
                {
                    environmentSlug = "*",
                    value = "x",
                    isEnabled = true,
                },
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Delete_Returns204_ThenNotFound_AndCascadesVersions()
    {
        var name = TestHelpers.UniqueName("del");
        await TestHelpers.CreateGlobalSecretAsync(Client, name, ("*", "v", true));

        var del = await Client.DeleteAsync($"/management/global/secrets/{name}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await Client.GetAsync($"/management/global/secrets/{name}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
        Assert.False(await db.GlobalSecretVersions.AnyAsync(v => v.GlobalSecret.Name == name));
    }
}
