using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grimoire.Cli.Export;

namespace Grimoire.Cli;

/// <summary>A failed Grimoire call, with the problem's detail when the API sent one.</summary>
public sealed class GrimoireApiException(HttpStatusCode status, string message)
    : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>The Consumer API calls the CLI makes.</summary>
internal sealed class GrimoireClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static HttpClient CreateHttpClient(Uri baseAddress, string token)
    {
        var http = new HttpClient { BaseAddress = baseAddress, Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-Api-Key", token);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("grimoire-cli");
        return http;
    }

    public async Task<string> GetSecretAsync(string name, string environment, CancellationToken ct)
    {
        var secret = await GetJsonAsync<SecretBody>(
            $"consumer/secrets/{Uri.EscapeDataString(name)}?environment={Escape(environment)}",
            ct
        );
        return secret.Value;
    }

    public async Task<IReadOnlyList<ConfigurationBody>> ListConfigurationAsync(
        string environment,
        CancellationToken ct
    ) =>
        (
            await GetJsonAsync<ConfigurationListBody>(
                $"consumer/configurations?environment={Escape(environment)}",
                ct
            )
        ).Items;

    /// <summary>The export in <paramref name="format"/>, exactly as the API sent it.</summary>
    public async Task<string> ExportAsync(ExportQuery query, string format, CancellationToken ct)
    {
        using var response = await http.GetAsync(query.Url(format), ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadAsStringAsync(ct);
    }

    /// <summary>The export as values, for <c>run</c>.</summary>
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
        using var response = await http.GetAsync(
            "consumer/secrets?environment=grimoire-cli-login-check",
            ct
        );
        return response.StatusCode != HttpStatusCode.Unauthorized;
    }

    private async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new GrimoireApiException(response.StatusCode, "Empty response from Grimoire.");
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
            detail = problem?.Detail ?? problem?.Title;
        }
        catch (JsonException)
        {
            // Not a problem document; fall back to the status.
        }
        throw new GrimoireApiException(
            response.StatusCode,
            detail ?? $"Grimoire answered {(int)response.StatusCode} {response.ReasonPhrase}."
        );
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record SecretBody(string Name, string Value);

    private sealed record ConfigurationListBody(IReadOnlyList<ConfigurationBody> Items);

    private sealed record ExportBody(
        Dictionary<string, string> Secrets,
        Dictionary<string, string> Configurations,
        IReadOnlyList<string> Skipped
    );

    private sealed record ProblemBody(string? Title, string? Detail);
}

internal sealed record ConfigurationBody(
    string Key,
    string Value,
    [property: JsonPropertyName("valueType")] string? ValueType
);

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
