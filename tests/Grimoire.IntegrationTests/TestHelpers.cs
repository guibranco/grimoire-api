using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests;

public static class TestHelpers
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public static string UniqueName(string prefix = "test") => $"{prefix}-{Guid.NewGuid():N}"[..24];

    public static async Task<(string slug, string apiKey)> CreateApplicationAsync(
        HttpClient mgmtClient,
        string? name = null
    )
    {
        name ??= UniqueName("app");
        var response = await mgmtClient.PostAsJsonAsync(
            "/api/management/applications",
            new { name, description = "Test app" }
        );
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return (json["slug"]!.GetValue<string>(), json["plainApiKey"]!.GetValue<string>());
    }

    public static async Task<string> CreateEnvironmentAsync(
        HttpClient mgmtClient,
        string appSlug,
        string envName = "staging"
    )
    {
        var response = await mgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{appSlug}/environments",
            new { name = envName }
        );
        response.EnsureSuccessStatusCode();
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        return json["slug"]!.GetValue<string>();
    }

    public static async Task CreateSecretWithValueAsync(
        HttpClient mgmtClient,
        string appSlug,
        string secretName,
        string envSlug,
        string value
    )
    {
        var create = await mgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{appSlug}/secrets",
            new { name = secretName, description = "test" }
        );
        create.EnsureSuccessStatusCode();

        var setValue = await mgmtClient.PostAsJsonAsync(
            $"/api/management/applications/{appSlug}/secrets/{secretName}/values",
            new[]
            {
                new
                {
                    environmentSlug = envSlug,
                    value,
                    isEnabled = true,
                },
            }
        );
        setValue.EnsureSuccessStatusCode();
    }

    public static async Task<T> ReadAsAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOpts))!;
}
