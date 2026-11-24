using System.Net;
using System.Text;
using System.Text.Json;
using Grimoire.Consumer;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Grimoire.Tests;

/// <summary>Sentinel-based refresh in the configuration provider (guibranco/grimoire-api#95).</summary>
public class ConfigurationRefreshTests
{
    /// <summary>A tiny Consumer API: configuration values plus ETags, with request counters.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal);
        public bool Fail { get; set; }
        public int ListCalls { get; private set; }
        public int KeyCalls { get; private set; }
        public int NotModified { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (Fail)
                throw new HttpRequestException("Grimoire is down.");

            var path = request.RequestUri!.AbsolutePath;
            if (path == "/consumer/configurations")
            {
                ListCalls++;
                var items = Values
                    .Where(v => v.Key is not ("Sentinel" or "Global:Sentinel"))
                    .Select(v => Item(v.Key, v.Value));
                return Task.FromResult(Json(new { items }));
            }

            KeyCalls++;
            var key = Uri.UnescapeDataString(path["/consumer/configurations/".Length..]);
            if (!Values.TryGetValue(key, out var value))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            var etag =
                $"\"{Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key + "=" + value)))[..16]}\"";
            if (request.Headers.IfNoneMatch.Any(t => t.Tag == etag))
            {
                NotModified++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified));
            }
            var response = Json(Item(key, value));
            response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
            return Task.FromResult(response);
        }

        private static object Item(string key, string value) =>
            new
            {
                key,
                value,
                label = "local",
                valueType = value.StartsWith('[') ? "array" : "string",
            };

        private static HttpResponseMessage Json(object body) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(body),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
    }

    private static (GrimoireConfigurationClient Provider, IConfigurationRoot Config) Build(
        FakeServer server,
        GrimoireRefreshOptions? refresh
    )
    {
        var provider = new GrimoireConfigurationClient(
            "http://grimoire.test",
            "grm_key",
            "local",
            server,
            refresh
        );
        var config = new ConfigurationBuilder().Add(provider).Build();
        return (provider, config);
    }

    private static GrimoireRefreshOptions SlowRefresh() =>
        // Long enough that the background loop never fires during a test.
        new GrimoireRefreshOptions().SetRefreshInterval(TimeSpan.FromHours(1));

    [Fact]
    public async Task WithoutRefresh_NothingIsWatched()
    {
        var server = new FakeServer { Values = { ["A"] = "1", ["Sentinel"] = "1" } };
        var (provider, config) = Build(server, refresh: null);

        Assert.False(await provider.TryRefreshAsync());
        Assert.Equal(0, server.KeyCalls);
        Assert.Equal("1", config["A"]);
    }

    [Fact]
    public async Task UnchangedSentinels_CostA304_AndDoNotReload()
    {
        var server = new FakeServer { Values = { ["A"] = "1", ["Sentinel"] = "100" } };
        var (provider, config) = Build(server, SlowRefresh());
        var reloads = 0;
        config.GetReloadToken().RegisterChangeCallback(_ => reloads++, null);

        Assert.False(await provider.TryRefreshAsync());

        Assert.Equal(1, server.ListCalls);
        Assert.Equal(1, server.NotModified);
        Assert.Equal(0, reloads);
        Assert.NotNull(provider.LastRefreshedAt);
    }

    [Fact]
    public async Task ChangedSentinel_ReloadsEverything_AndFiresTheReloadToken()
    {
        var server = new FakeServer { Values = { ["A"] = "1", ["Sentinel"] = "100" } };
        var (provider, config) = Build(server, SlowRefresh());
        var reloaded = false;
        config.GetReloadToken().RegisterChangeCallback(_ => reloaded = true, null);

        server.Values["A"] = "2";
        server.Values["B"] = "new";
        Assert.False(await provider.TryRefreshAsync()); // staged: sentinel not bumped yet
        Assert.Equal("1", config["A"]);

        server.Values["Sentinel"] = "101";
        Assert.True(await provider.TryRefreshAsync());

        Assert.True(reloaded);
        Assert.Equal("2", config["A"]);
        Assert.Equal("new", config["B"]);
        Assert.Equal(2, server.ListCalls);
    }

    [Fact]
    public async Task GlobalSentinelAppearing_Reloads()
    {
        var server = new FakeServer { Values = { ["A"] = "1" } };
        var (provider, config) = Build(server, SlowRefresh());

        server.Values["Shared"] = "x";
        server.Values["Global:Sentinel"] = "5";

        Assert.True(await provider.TryRefreshAsync());
        Assert.Equal("x", config["Shared"]);
    }

    [Fact]
    public async Task Failure_IsThrown_AndKeepsTheLastValues()
    {
        var server = new FakeServer { Values = { ["A"] = "1", ["Sentinel"] = "100" } };
        var (provider, config) = Build(server, SlowRefresh());

        server.Fail = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => provider.TryRefreshAsync());

        Assert.Equal("1", config["A"]);
    }

    [Fact]
    public async Task KeyRegisteredWithoutRefreshAll_UpdatesOnlyThatKey()
    {
        var server = new FakeServer { Values = { ["A"] = "1", ["Ips"] = "[\"10.0.0.1\"]" } };
        var refresh = SlowRefresh().Register("Ips");
        var (provider, config) = Build(server, refresh);
        Assert.Equal("10.0.0.1", config["Ips:0"]);

        server.Values["A"] = "2";
        server.Values["Ips"] = "[\"10.0.0.2\",\"10.0.0.3\"]";
        Assert.True(await provider.TryRefreshAsync());

        Assert.Equal("1", config["A"]); // not reloaded
        Assert.Equal(["10.0.0.2", "10.0.0.3"], config.GetSection("Ips").Get<string[]>()!);
        Assert.Equal(1, server.ListCalls);
    }

    [Fact]
    public void Defaults_WatchBothSentinels_AndRegisterReplacesThem()
    {
        var defaults = new GrimoireRefreshOptions();
        Assert.Equal(
            new Dictionary<string, bool> { ["Sentinel"] = true, ["Global:Sentinel"] = true },
            defaults.WatchedKeys
        );
        Assert.Equal(TimeSpan.FromSeconds(30), defaults.RefreshInterval);

        var custom = new GrimoireRefreshOptions().Register("Sentinel", refreshAll: true);
        Assert.Equal(["Sentinel"], custom.WatchedKeys.Keys);
    }

    [Fact]
    public void RefreshInterval_BelowOneSecond_IsRejected() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new GrimoireRefreshOptions().SetRefreshInterval(TimeSpan.FromMilliseconds(100))
        );

    [Fact]
    public void NextDelay_AddsJitter_AndBacksOffUpToFiveMinutes()
    {
        var provider = new GrimoireConfigurationClient(
            "http://grimoire.test",
            "k",
            "local",
            new FakeServer(),
            new GrimoireRefreshOptions().SetRefreshInterval(TimeSpan.FromSeconds(10))
        );

        for (var i = 0; i < 20; i++)
        {
            var delay = provider.NextDelay(0);
            Assert.InRange(delay, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(12));
        }
        Assert.Equal(TimeSpan.FromSeconds(20), provider.NextDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(80), provider.NextDelay(3));
        Assert.Equal(TimeSpan.FromMinutes(5), provider.NextDelay(10));
    }

    [Fact]
    public void AddGrimoire_WithOptions_RequiresConnect() =>
        Assert.Throws<InvalidOperationException>(() =>
            new ConfigurationBuilder().AddGrimoire(o => o.ConfigureRefresh())
        );

    [Fact]
    public void AddGrimoire_WithOptions_RegistersTheProvider()
    {
        var builder = new ConfigurationBuilder().AddGrimoire(o =>
            o.Connect("http://localhost:5000", "k", "local").ConfigureRefresh()
        );

        Assert.Contains(builder.Sources, s => s is GrimoireConfigurationClient);
    }
}
