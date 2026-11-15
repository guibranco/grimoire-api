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
            "/management/applications",
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
            $"/management/applications/{appSlug}/environments",
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
            $"/management/applications/{appSlug}/secrets",
            new { name = secretName, description = "test" }
        );
        create.EnsureSuccessStatusCode();

        var setValue = await mgmtClient.PostAsJsonAsync(
            $"/management/applications/{appSlug}/secrets/{secretName}/values",
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

    public static async Task CreateConfigurationAsync(
        HttpClient mgmtClient,
        string appSlug,
        string envSlug,
        string key,
        string value
    )
    {
        var response = await mgmtClient.PostAsJsonAsync(
            $"/management/applications/{appSlug}/configurations",
            new
            {
                environmentSlug = envSlug,
                key,
                value,
            }
        );
        response.EnsureSuccessStatusCode();
    }

    public static async Task CreateGlobalConfigurationAsync(
        HttpClient mgmtClient,
        string key,
        string value,
        string? environmentSlug = null
    )
    {
        var response = await mgmtClient.PostAsJsonAsync(
            "/management/global/configurations",
            new
            {
                environmentSlug,
                key,
                value,
            }
        );
        response.EnsureSuccessStatusCode();
    }

    public static async Task CreateGlobalSecretAsync(
        HttpClient mgmtClient,
        string name,
        params (string environmentSlug, string value, bool isEnabled)[] values
    )
    {
        var create = await mgmtClient.PostAsJsonAsync(
            "/management/global/secrets",
            new { name, description = "global test" }
        );
        create.EnsureSuccessStatusCode();

        if (values.Length == 0)
            return;

        var setValues = await mgmtClient.PostAsJsonAsync(
            $"/management/global/secrets/{name}/values",
            values.Select(v => new
            {
                v.environmentSlug,
                v.value,
                v.isEnabled,
            })
        );
        setValues.EnsureSuccessStatusCode();
    }

    public static async Task<T> ReadAsAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(JsonOpts))!;
}
