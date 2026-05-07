using Grimoire.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Grimoire.IntegrationTests;

public sealed class GrimoireWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"grimoire-test-{Guid.NewGuid():N}.db");

    public const string AdminKey = "test-admin-key-integration";
    public const string MasterKey = "test-master-key-32-chars-minimum!";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Management:AdminApiKey"] = AdminKey,
                ["Encryption:MasterKey"] = MasterKey,
                ["Cors:AllowedOrigins:0"] = "http://localhost:5173"
            });
        });
    }

    public HttpClient CreateManagementClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminKey}");
        return client;
    }

    public HttpClient CreateConsumerClient(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
        return client;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        TryDeleteDb();
    }

    private void TryDeleteDb()
    {
        foreach (var f in new[] { _dbPath, _dbPath + "-shm", _dbPath + "-wal" })
            try { File.Delete(f); } catch { /* ignore */ }
    }
}
