using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Grimoire.Cli;

/// <summary>A failed Grimoire call, with the problem's detail when the API sent one.</summary>
public sealed class GrimoireApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}

/// <summary>JSON calls and problem-detail errors shared by the consumer and management clients.</summary>
internal abstract class ApiClient(HttpClient http)
{
    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected HttpClient Http => http;

    protected async Task<T> GetJsonAsync<T>(string url, CancellationToken ct)
    {
        using var response = await http.GetAsync(url, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    protected async Task<T> SendJsonAsync<T>(
        HttpMethod method,
        string url,
        object body,
        CancellationToken ct
    )
    {
        using var response = await SendAsync(method, url, body, ct);
        return await ReadJsonAsync<T>(response, ct);
    }

    /// <summary>The raw response, for callers that branch on the status.</summary>
    protected Task<HttpResponseMessage> SendAsync(
        HttpMethod method,
        string url,
        object body,
        CancellationToken ct
    ) =>
        http.SendAsync(
            new HttpRequestMessage(method, url)
            {
                Content = JsonContent.Create(body, options: Json),
            },
            ct
        );

    protected static async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        await EnsureSuccessAsync(response, ct);
        return await response.Content.ReadFromJsonAsync<T>(Json, ct)
            ?? throw new GrimoireApiException(response.StatusCode, "Empty response from Grimoire.");
    }

    protected static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        CancellationToken ct
    )
    {
        if (response.IsSuccessStatusCode)
            return;
        string? detail = null;
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(Json, ct);
            detail = problem?.Detail ?? problem?.Title;
            // Validation problems carry the useful part in `errors`.
            if (problem?.Errors is { Count: > 0 } errors)
                detail = string.Join(" ", errors.SelectMany(e => e.Value));
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

    protected static string Escape(string value) => Uri.EscapeDataString(value);

    private sealed record ProblemBody(
        string? Title,
        string? Detail,
        Dictionary<string, string[]>? Errors
    );
}
