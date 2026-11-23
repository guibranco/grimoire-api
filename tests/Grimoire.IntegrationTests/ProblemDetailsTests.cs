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
        };

    [Theory]
    [MemberData(nameof(NotFoundRoutes))]
    public async Task NotFound_IsATopLevelProblem(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "PUT")
            request.Content = JsonContent.Create(new { name = "x" });
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
