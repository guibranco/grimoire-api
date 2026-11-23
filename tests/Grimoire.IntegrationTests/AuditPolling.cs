using System.Text.Json.Nodes;

namespace Grimoire.IntegrationTests;

/// <summary>Audit events are written in the background, so tests poll for them.</summary>
public static class AuditPolling
{
    /// <summary>The audit items matching <paramref name="query"/>, once there are <paramref name="count"/> (or after 10 s).</summary>
    public static async Task<List<JsonNode>> WaitForAuditAsync(
        this HttpClient client,
        string query,
        int count = 1
    )
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            var page = JsonNode.Parse(await client.GetStringAsync($"/management/audit?{query}"))!;
            var items = page["items"]!.AsArray().Select(i => i!).ToList();
            if (items.Count >= count || DateTime.UtcNow > deadline)
                return items;
            await Task.Delay(50);
        }
    }
}
