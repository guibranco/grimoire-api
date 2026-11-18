using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;

namespace Grimoire.Consumer;

public class GrimoireConfigurationClient : ConfigurationProvider, IConfigurationSource
{
    private readonly string _baseUrl;
    private readonly string _apiKey;
    private readonly string _environment;
    private readonly HttpMessageHandler? _handler;

    public GrimoireConfigurationClient(string baseUrl, string apiKey, string environment)
        : this(baseUrl, apiKey, environment, null) { }

    internal GrimoireConfigurationClient(
        string baseUrl,
        string apiKey,
        string environment,
        HttpMessageHandler? handler
    )
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _apiKey = apiKey;
        _environment = environment;
        _handler = handler;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder) => this;

    /// <summary>
    /// All configuration values as stored in Grimoire. Array and JSON values are returned as their
    /// raw JSON text; use <see cref="Load"/> (through <c>AddGrimoire</c>) to get them flattened
    /// into configuration sections.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, string>> GetAllAsync(
        CancellationToken ct = default
    ) => (await FetchAllAsync(ct)).ToDictionary(i => i.Key, i => i.Value);

    /// <summary>The raw value of <paramref name="key"/> (JSON text for array and JSON values).</summary>
    public async Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        (await FetchAsync(key, ct))?.Value;

    /// <summary>
    /// The value of <paramref name="key"/> deserialised to <typeparamref name="T"/>: JSON for
    /// array and JSON values (for example <c>string[]</c> or an options class), and a JSON
    /// string literal for string and multiline values. Returns <c>default</c> when the key is missing.
    /// </summary>
    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var item = await FetchAsync(key, ct);
        if (item is null)
            return default;
        return IsStructured(item.ValueType)
            ? JsonSerializer.Deserialize<T>(item.Value, JsonOptions)
            : JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(item.Value), JsonOptions);
    }

    private async Task<ConfigItem?> FetchAsync(string key, CancellationToken ct)
    {
        using var http = CreateClient();
        var response = await http.GetAsync(
            $"consumer/configurations/{Uri.EscapeDataString(key)}?environment={Uri.EscapeDataString(_environment)}",
            ct
        );
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ConfigItem>(ct);
    }

    /// <summary>
    /// Loads every value into configuration. Array and JSON values are flattened the same way as
    /// <c>appsettings.json</c>, so <c>AllowedIps</c> = <c>["a","b"]</c> becomes
    /// <c>AllowedIps:0</c> and <c>AllowedIps:1</c>, and binds with
    /// <c>GetSection("AllowedIps").Get&lt;string[]&gt;()</c>.
    /// </summary>
    public override void Load()
    {
        var items = FetchAllAsync(CancellationToken.None).GetAwaiter().GetResult();
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
        {
            if (IsStructured(item.ValueType) && TryFlatten(item.Key, item.Value, data))
                continue;
            data[item.Key] = item.Value;
        }
        Data = data;
    }

    private static bool IsStructured(string? valueType) =>
        string.Equals(valueType, "array", StringComparison.OrdinalIgnoreCase)
        || string.Equals(valueType, "json", StringComparison.OrdinalIgnoreCase);

    private static bool TryFlatten(string key, string json, IDictionary<string, string?> data)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            // Not valid JSON after all: fall back to the raw value rather than failing to start.
            return false;
        }

        using (doc)
            Flatten(key, doc.RootElement, data);
        return true;
    }

    private static void Flatten(string path, JsonElement element, IDictionary<string, string?> data)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var hasProperties = false;
                foreach (var property in element.EnumerateObject())
                {
                    hasProperties = true;
                    Flatten(ConfigurationPath.Combine(path, property.Name), property.Value, data);
                }
                // Like the JSON file provider: an empty object still shows up as a section.
                if (!hasProperties)
                    data[path] = null;
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Flatten(ConfigurationPath.Combine(path, index++.ToString()), item, data);
                if (index == 0)
                    data[path] = null;
                break;
            case JsonValueKind.String:
                data[path] = element.GetString();
                break;
            case JsonValueKind.Null:
                data[path] = null;
                break;
            default:
                // Numbers and booleans keep their JSON text ("3", "true"), which binds as usual.
                data[path] = element.GetRawText();
                break;
        }
    }

    private async Task<IReadOnlyList<ConfigItem>> FetchAllAsync(CancellationToken ct)
    {
        using var http = CreateClient();
        var response = await http.GetAsync(
            $"consumer/configurations?environment={Uri.EscapeDataString(_environment)}",
            ct
        );
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ConfigListResponse>(ct);
        return result?.Items ?? [];
    }

    private HttpClient CreateClient()
    {
        var http = _handler is null ? new HttpClient() : new HttpClient(_handler, false);
        http.BaseAddress = new Uri(_baseUrl + "/");
        http.DefaultRequestHeaders.Add("X-Api-Key", _apiKey);
        return http;
    }

    private record ConfigItem(
        [property: JsonPropertyName("key")] string Key,
        [property: JsonPropertyName("value")] string Value,
        [property: JsonPropertyName("label")] string Label,
        // Missing from servers older than value types: treated as "string".
        [property: JsonPropertyName("valueType")] string? ValueType = null
    );

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private record ConfigListResponse([property: JsonPropertyName("items")] List<ConfigItem> Items);
}
