using System.Net;
using System.Text.Json.Serialization;
using Grimoire.Cli.Export;

namespace Grimoire.Cli;

/// <summary>The Consumer API calls the CLI makes, authenticated with an API key or access token.</summary>
internal sealed class GrimoireClient : ApiClient
{
    public GrimoireClient(HttpClient http, string token)
        : base(http)
    {
        http.DefaultRequestHeaders.Remove("X-Api-Key");
        http.DefaultRequestHeaders.Add("X-Api-Key", token);
    }

    public async Task<string> GetSecretAsync(string name, string environment, CancellationToken ct)
    {
        var secret = await GetJsonAsync<SecretBody>(
            $"consumer/secrets/{Escape(name)}?environment={Escape(environment)}",
            ct
        );
        return secret.Value;
    }

    public async Task<IReadOnlyList<SecretListItem>> ListSecretsAsync(
        string environment,
        CancellationToken ct
    ) =>
        (
            await GetJsonAsync<ListBody<SecretListItem>>(
                $"consumer/secrets?environment={Escape(environment)}",
                ct
            )
        ).Items;

    public async Task<IReadOnlyList<ConfigurationBody>> ListConfigurationAsync(
        string environment,
        CancellationToken ct
    ) =>
        (
            await GetJsonAsync<ListBody<ConfigurationBody>>(
                $"consumer/configurations?environment={Escape(environment)}",
                ct
            )
        ).Items;

    /// <summary>Each effective configuration key's value type (<c>string</c> when the server sends none).</summary>
    public async Task<IReadOnlyDictionary<string, string>> ConfigurationTypesAsync(
        string environment,
        CancellationToken ct
    ) =>
        (await ListConfigurationAsync(environment, ct)).ToDictionary(
            c => c.Key,
            c => c.ValueType ?? "string",
            StringComparer.Ordinal
        );

    /// <summary>Everything the export query selects, as values.</summary>
    public async Task<(ExportedValues Values, IReadOnlyList<string> Skipped)> ExportValuesAsync(
        ExportQuery query,
        CancellationToken ct
    )
    {
        var body = await GetJsonAsync<ExportBody>(query.Url("json"), ct);
        return (new ExportedValues(body.Secrets, body.Configurations), body.Skipped);
    }

    /// <summary>
    /// Whether the API accepts the token. Any answer but <c>401</c> means it does: a probe for
    /// an environment that doesn't exist gets <c>404</c> once the token has passed.
    /// </summary>
    public async Task<bool> AcceptsTokenAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(
            "consumer/secrets?environment=grimoire-cli-login-check",
            ct
        );
        return response.StatusCode != HttpStatusCode.Unauthorized;
    }

    private sealed record SecretBody(string Name, string Value);

    private sealed record ListBody<T>(IReadOnlyList<T> Items);

    private sealed record ExportBody(
        Dictionary<string, string> Secrets,
        Dictionary<string, string> Configurations,
        IReadOnlyList<string> Skipped
    );
}

internal sealed record ConfigurationBody(
    string Key,
    string Value,
    [property: JsonPropertyName("valueType")] string? ValueType
);

internal sealed record SecretListItem(string Name, string Source, bool Active, int? Version);

/// <summary>The environment and filters of an export.</summary>
internal sealed record ExportQuery(string Environment, string? Include, string? Prefix)
{
    public string Url(string format)
    {
        var url =
            $"consumer/export?environment={Uri.EscapeDataString(Environment)}&format={format}";
        if (!string.IsNullOrWhiteSpace(Include))
            url += $"&include={Uri.EscapeDataString(Include)}";
        if (!string.IsNullOrEmpty(Prefix))
            url += $"&prefix={Uri.EscapeDataString(Prefix)}";
        return url;
    }
}
