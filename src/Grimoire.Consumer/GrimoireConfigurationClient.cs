using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Consumer;

public class GrimoireConfigurationClient : ConfigurationProvider, IConfigurationSource
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _environment;

    public GrimoireConfigurationClient(string baseUrl, string apiKey, string environment)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _environment = environment;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken ct = default) =>
        FetchAllAsync(ct);

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var http = CreateClient();
        var response = await http.GetAsync(
            $"api/consumer/configurations/{Uri.EscapeDataString(key)}?environment={Uri.EscapeDataString(_environment)}",
            ct
        );
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var item = await response.Content.ReadFromJsonAsync<ConfigItem>(ct);
        return item?.Value;
    }

    public override void Load()
    {
        var result = FetchAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        Data = result.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value,
            StringComparer.OrdinalIgnoreCase
        )!;
    }

    private async Task<IReadOnlyDictionary<string, string>> FetchAllAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        var response = await http.GetAsync(
            $"api/consumer/configurations?environment={Uri.EscapeDataString(_environment)}",
            ct
        );
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConfigListResponse>(ct);
        return result?.Items.ToDictionary(i => i.Key, i => i.Value)
            ?? new Dictionary<string, string>();
    }

    private HttpClient CreateClient()
    {
        var http = new HttpClient { BaseAddress = new Uri(_baseUrl + "/") };
        http.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        return http;
    }

    private record ConfigItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("label")] string Label
    );

    private record ConfigListResponse([property: JsonPropertyName("items")] List<ConfigItem> Items);
}
