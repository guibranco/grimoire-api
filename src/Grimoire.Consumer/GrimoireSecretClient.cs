using System.Net.Http.Json;
using Grimoire.Consumer.Models;

namespace Grimoire.Consumer;

public class GrimoireSecretClient
{
    private readonly HttpClient _http;
    private readonly string _environment;

    public GrimoireSecretClient(string baseUrl, string apiKey, string environment)
    {
        _environment = environment;
        _http = new HttpClient { BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/") };
        _http.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
    }

    public async Task<GrimoireSecret> GetSecretAsync(string name, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"api/consumer/secrets/{Uri.EscapeDataString(name)}?environment={Uri.EscapeDataString(_environment)}", ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GrimoireSecret>(ct))!;
    }
}
