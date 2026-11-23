using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests;

/// <summary>
/// Error responses are top-level RFC 9457 problem details whose <c>status</c> matches the HTTP
/// status, so clients can read <c>detail</c>. (Wrapping <c>Problem(...)</c> in <c>NotFound(...)</c>
/// serialised the inner result object instead.)
/// </summary>
public class ProblemDetailsTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>
{
    private HttpClient Client => factory.CreateManagementClient();

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        HttpStatusCode expected
    )
    {
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(body);
        Assert.Equal((int)expected, body["status"]!.GetValue<int>());
        Assert.False(string.IsNullOrWhiteSpace(body["detail"]?.GetValue<string>()));
        Assert.Null(body["value"]);
        Assert.Null(body["statusCode"]);
    }

    public static TheoryData<string, string> NotFoundRoutes =>
        new()
        {
            { "GET", "/management/applications/nope" },
            { "PUT", "/management/applications/nope" },
            { "DELETE", "/management/applications/nope" },
            { "GET", "/management/applications/nope/environments" },
            { "GET", "/management/applications/nope/secrets" },
            { "GET", "/management/applications/nope/secrets/effective" },
            { "GET", "/management/applications/nope/configurations" },
            { "GET", "/management/applications/nope/configurations/effective?environment=local" },
            { "GET", "/management/global/secrets/NOPE" },
            { "DELETE", "/management/global/secrets/NOPE" },
            { "GET", "/management/global/secrets/NOPE/versions/*" },
            { "PATCH", "/management/global/secrets/NOPE" },
            { "POST", "/management/global/secrets/NOPE/values" },
            { "PATCH", "/management/global/secrets/NOPE/versions/*/1" },
            { "POST", "/management/applications/nope/rotate-key" },
            { "POST", "/management/applications/nope/secrets" },
            { "GET", "/management/applications/nope/secrets/S" },
            { "PATCH", "/management/applications/nope/secrets/S" },
            { "DELETE", "/management/applications/nope/secrets/S" },
            { "POST", "/management/applications/nope/secrets/S/values" },
            { "GET", "/management/applications/nope/secrets/S/versions/local" },
            { "PATCH", "/management/applications/nope/secrets/S/versions/local/1" },
            { "POST", "/management/applications/nope/configurations" },
            { "PUT", "/management/applications/nope/configurations/local/K" },
            { "DELETE", "/management/applications/nope/configurations/local/K" },
            { "POST", "/management/applications/nope/environments" },
            { "DELETE", "/management/applications/nope/environments/local" },
        };

    /// <summary>A body each route accepts, so the request reaches the lookup instead of validation.</summary>
    private static HttpContent? BodyFor(string method, string path)
    {
        if (method is "GET" or "DELETE")
            return null;
        if (path.EndsWith("/values"))
            return JsonContent.Create(new[] { new { environmentSlug = "local", value = "v" } });
        if (path.EndsWith("/configurations"))
            return JsonContent.Create(
                new
                {
                    environmentSlug = "local",
                    key = "K",
                    value = "v",
                }
            );
        if (path.Contains("/configurations/"))
            return JsonContent.Create(new { value = "v" });
        if (path.EndsWith("/environments"))
            return JsonContent.Create(new { name = "qa" });
        if (path.Contains("/versions/"))
            return JsonContent.Create(new { isEnabled = true });
        return JsonContent.Create(new { name = "x", description = "d" });
    }

    [Theory]
    [MemberData(nameof(NotFoundRoutes))]
    public async Task NotFound_IsATopLevelProblem(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path)
        {
            Content = BodyFor(method, path),
        };
        await AssertProblemAsync(await Client.SendAsync(request), HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UnknownSecretAndEnvironment_AreTopLevelProblems()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await AssertProblemAsync(
            await Client.GetAsync($"/management/applications/{slug}/secrets/NOPE"),
            HttpStatusCode.NotFound
        );
        await AssertProblemAsync(
            await Client.DeleteAsync($"/management/applications/{slug}/secrets/NOPE"),
            HttpStatusCode.NotFound
        );
        await AssertProblemAsync(
            await Client.DeleteAsync($"/management/applications/{slug}/environments/nope"),
            HttpStatusCode.NotFound
        );

        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "S1", "local", "v");
        await AssertProblemAsync(
            await Client.PostAsJsonAsync(
                $"/management/applications/{slug}/secrets/S1/values",
                new[] { new { environmentSlug = "nope", value = "v" } }
            ),
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task MissingChildResources_AreTopLevelProblems()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "S1", "local", "v");
        var app = $"/management/applications/{slug}";

        foreach (
            var (method, path) in new[]
            {
                ("GET", $"{app}/configurations/effective?environment=nope"),
                ("POST", $"{app}/configurations"),
                ("PUT", $"{app}/configurations/nope/K"),
                ("PUT", $"{app}/configurations/local/MISSING"),
                ("DELETE", $"{app}/configurations/nope/K"),
                ("DELETE", $"{app}/configurations/local/MISSING"),
                ("GET", $"{app}/secrets/S1/versions/nope"),
                ("PATCH", $"{app}/secrets/S1/versions/nope/1"),
                ("PATCH", $"{app}/secrets/MISSING/versions/local/1"),
                ("POST", $"{app}/secrets/MISSING/values"),
            }
        )
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), path)
            {
                Content =
                    method == "POST" && path.EndsWith("/configurations")
                        ? JsonContent.Create(
                            new
                            {
                                environmentSlug = "nope",
                                key = "K",
                                value = "v",
                            }
                        )
                        : BodyFor(method, path),
            };
            await AssertProblemAsync(await Client.SendAsync(request), HttpStatusCode.NotFound);
        }

        var consumer = factory.CreateConsumerClient(apiKey);
        await AssertProblemAsync(
            await consumer.GetAsync("/consumer/configurations?environment=nope"),
            HttpStatusCode.NotFound
        );
        await AssertProblemAsync(
            await consumer.GetAsync("/consumer/secrets/S1?environment=nope"),
            HttpStatusCode.NotFound
        );
        await AssertProblemAsync(
            await consumer.GetAsync("/consumer/configurations/K?environment=nope"),
            HttpStatusCode.NotFound
        );
        await AssertProblemAsync(
            await consumer.GetAsync("/consumer/configurations/MISSING?environment=local"),
            HttpStatusCode.NotFound
        );
    }

    [Fact]
    public async Task Conflicts_AreTopLevelProblems()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Client);
        await TestHelpers.CreateSecretWithValueAsync(Client, slug, "DUP", "local", "v");

        await AssertProblemAsync(
            await Client.PostAsJsonAsync(
                $"/management/applications/{slug}/secrets",
                new { name = "DUP" }
            ),
            HttpStatusCode.Conflict
        );
        await AssertProblemAsync(
            await Client.PostAsJsonAsync(
                $"/management/applications/{slug}/environments",
                new { name = "local" }
            ),
            HttpStatusCode.Conflict
        );

        var name = TestHelpers.UniqueName("app");
        (
            await Client.PostAsJsonAsync("/management/applications", new { name })
        ).EnsureSuccessStatusCode();
        await AssertProblemAsync(
            await Client.PostAsJsonAsync("/management/applications", new { name }),
            HttpStatusCode.Conflict
        );
    }

    [Fact]
    public async Task ConsumerNotFound_IsATopLevelProblem()
    {
        var (_, apiKey) = await TestHelpers.CreateApplicationAsync(Client);
        var consumer = factory.CreateConsumerClient(apiKey);
        await AssertProblemAsync(
            await consumer.GetAsync("/consumer/secrets/NOPE?environment=local"),
            HttpStatusCode.NotFound
        );
    }
}
