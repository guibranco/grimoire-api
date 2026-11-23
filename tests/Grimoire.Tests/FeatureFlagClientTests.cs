using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grimoire.Consumer.FeatureFlags;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.FeatureManagement;
using Microsoft.FeatureManagement.FeatureFilters;

namespace Grimoire.Tests;

/// <summary><see cref="GrimoireFeatureFlagClient"/> and the Microsoft.FeatureManagement integration.</summary>
public class FeatureFlagClientTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 23, 9, 0, 0, TimeSpan.Zero);

    private static readonly Lazy<JsonNode> Vectors = new(() =>
        JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "FeatureFlags", "test-vectors.json")
            )
        )!
    );

    /// <summary>The vectors' flags as a Consumer API response.</summary>
    private static string VectorFlagsJson()
    {
        var flags = new JsonArray();
        foreach (var (_, f) in Vectors.Value["flags"]!.AsObject())
        {
            var state = f!["state"]!;
            flags.Add(
                new JsonObject
                {
                    ["key"] = f["key"]!.DeepClone(),
                    ["type"] = f["type"]!.DeepClone(),
                    ["variants"] = f["variants"]!.DeepClone(),
                    ["defaultVariant"] = f["defaultVariant"]!.DeepClone(),
                    ["salt"] = f["salt"]!.DeepClone(),
                    ["enabled"] = state["enabled"]!.DeepClone(),
                    ["offVariant"] = state["offVariant"]?.DeepClone(),
                    ["rules"] = state["rules"]!.DeepClone(),
                    ["fallthrough"] = state["fallthrough"]?.DeepClone(),
                    ["version"] = 1,
                }
            );
        }
        return new JsonObject { ["environment"] = "production", ["flags"] = flags }.ToJsonString();
    }

    private static string Flags(params (string Key, bool Enabled, string Variants)[] flags) =>
        $$"""{"environment":"production","flags":[{{string.Join(",", flags.Select(f =>
            $$"""{"key":"{{f.Key}}","type":"multivariate","variants":{{f.Variants}},"defaultVariant":"off","salt":"s","enabled":{{(f.Enabled ? "true" : "false")}},"offVariant":null,"rules":[],"fallthrough":{"variant":"on"},"version":1}"""
        ))}}]}""";

    private const string OnOff = """[{"key":"on","value":true},{"key":"off","value":false}]""";

    /// <summary>Answers with the queued responses (the last one repeats), recording requests.</summary>
    private sealed class FakeServer : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _responses = new();
        private Func<HttpRequestMessage, HttpResponseMessage>? _last;
        public List<HttpRequestMessage> Requests { get; } = [];

        public FakeServer Then(HttpStatusCode status, string? json = null, string? etag = null)
        {
            _responses.Enqueue(_ =>
            {
                var response = new HttpResponseMessage(status);
                if (json is not null)
                    response.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (etag is not null)
                    response.Headers.ETag = new System.Net.Http.Headers.EntityTagHeaderValue(etag);
                return response;
            });
            return this;
        }

        public FakeServer ThenFail()
        {
            _responses.Enqueue(_ => throw new HttpRequestException("down"));
            return this;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Requests.Add(request);
            if (_responses.Count > 0)
                _last = _responses.Dequeue();
            return Task.FromResult(_last!(request));
        }
    }

    private static GrimoireFeatureFlagClient Client(FakeServer server, TimeProvider? time = null) =>
        new(
            new GrimoireFeatureFlagOptions
            {
                BaseUrl = "https://grimoire.test/",
                ApiKey = "grm_key",
                Environment = "prod env",
                RefreshInterval = TimeSpan.FromSeconds(30),
            },
            new HttpClient(server),
            time ?? new FakeTimeProvider(Start)
        );

    public static TheoryData<int> CaseIndexes()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < Vectors.Value["cases"]!.AsArray().Count; i++)
            data.Add(i);
        return data;
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public async Task SharedVectors_ThroughTheClient(int index)
    {
        using var client = Client(new FakeServer().Then(HttpStatusCode.OK, VectorFlagsJson()));
        var c = Vectors.Value["cases"]![index]!;
        var context = new FlagContext();
        foreach (var (name, value) in c["context"]!.AsObject())
            context.With(name, value!.GetValue<string>());
        var expected = c["expected"]!;

        var result = await client.EvaluateAsync(c["flag"]!.GetValue<string>(), context);

        Assert.Equal(expected["variant"]!.GetValue<string>(), result.Variant);
        Assert.Equal(expected["reason"]!.GetValue<string>(), result.Reason);
        Assert.Equal(expected["matchedRuleIndex"]?.GetValue<int>(), result.MatchedRuleIndex);
        Assert.Equal(expected["bucket"]?.GetValue<int>(), result.Bucket);
        Assert.True(result.Found);
    }

    [Fact]
    public async Task FetchesOnce_WithTheApiKey_AndServesValues()
    {
        var server = new FakeServer().Then(HttpStatusCode.OK, VectorFlagsJson(), "\"v1\"");
        using var client = Client(server);

        var checkout = await client.EvaluateAsync("checkout-redesign", new FlagContext("u-123"));
        Assert.Equal("one-page", checkout.ValueAs<JsonElement>().GetProperty("layout").GetString());
        Assert.Equal(
            "treatment",
            await client.GetVariantAsync("checkout-redesign", new FlagContext("u-123"))
        );
        Assert.Equal("smtp", await client.GetValueAsync<string>("email-provider"));
        Assert.False(await client.IsEnabledAsync("kill-switch"));
        Assert.True(
            await client.IsEnabledAsync(
                "beta-dashboard",
                new FlagContext().With("appVersion", "2.1.0")
            )
        );
        Assert.Contains("gradual-rollout", await client.GetFlagKeysAsync());
        Assert.Equal(Start, client.LastRefreshedAt);

        var request = Assert.Single(server.Requests);
        Assert.Equal(
            "https://grimoire.test/consumer/feature-flags?environment=prod%20env",
            request.RequestUri!.AbsoluteUri
        );
        Assert.Equal("grm_key", request.Headers.GetValues("X-Api-Key").Single());
    }

    [Fact]
    public async Task UnknownFlag_IsOff_WithFallbacks()
    {
        using var client = Client(
            new FakeServer().Then(HttpStatusCode.OK, Flags(("a", true, OnOff)))
        );
        var result = await client.EvaluateAsync("nope");
        Assert.Equal((false, FeatureFlagReasons.FlagNotFound), (result.Found, result.Reason));
        Assert.False(result.IsEnabled);
        Assert.Null(await client.GetVariantAsync("nope"));
        Assert.Equal(7, await client.GetValueAsync("nope", fallback: 7));
        // Wrong type → fallback rather than an exception.
        Assert.Equal(3, await client.GetValueAsync("a", fallback: 3));
        Assert.True(await client.GetValueAsync<bool>("a"));
    }

    [Fact]
    public async Task Refresh_SendsTheEtag_AndKeepsDefinitionsOn304()
    {
        var server = new FakeServer()
            .Then(HttpStatusCode.OK, Flags(("a", true, OnOff)), "\"v1\"")
            .Then(HttpStatusCode.NotModified)
            .Then(HttpStatusCode.OK, Flags(("a", false, OnOff)), "\"v2\"");
        var time = new FakeTimeProvider(Start);
        using var client = Client(server, time);

        Assert.True(await client.RefreshAsync());
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.False(await client.RefreshAsync());
        Assert.Equal("\"v1\"", server.Requests[1].Headers.IfNoneMatch.Single().Tag);
        Assert.Equal(Start.AddSeconds(5), client.LastRefreshedAt);
        Assert.True(await client.IsEnabledAsync("a"));

        Assert.True(await client.RefreshAsync());
        Assert.False(await client.IsEnabledAsync("a"));
    }

    [Fact]
    public async Task StaleDefinitions_RefreshInTheBackground()
    {
        var server = new FakeServer()
            .Then(HttpStatusCode.OK, Flags(("a", true, OnOff)), "\"v1\"")
            .Then(HttpStatusCode.OK, Flags(("a", false, OnOff)), "\"v2\"");
        var time = new FakeTimeProvider(Start);
        using var client = Client(server, time);

        Assert.True(await client.IsEnabledAsync("a"));
        Assert.True(await client.IsEnabledAsync("a"));
        Assert.Single(server.Requests);

        time.Advance(TimeSpan.FromSeconds(31));
        // The stale read is served from the cache while the refresh runs.
        Assert.True(await client.IsEnabledAsync("a"));
        await client.WaitForBackgroundRefreshAsync();
        Assert.Equal(2, server.Requests.Count);
        Assert.False(await client.IsEnabledAsync("a"));
    }

    [Fact]
    public async Task KeepsWorking_WhenGrimoireIsDown()
    {
        var server = new FakeServer()
            .ThenFail()
            .Then(HttpStatusCode.OK, Flags(("a", true, OnOff)))
            .ThenFail();
        var time = new FakeTimeProvider(Start);
        using var client = Client(server, time);

        // Never loaded: defaults, no exception.
        Assert.False(await client.IsEnabledAsync("a"));
        Assert.Null(client.LastRefreshedAt);

        // Loads on the next call.
        Assert.True(await client.IsEnabledAsync("a"));

        // Down again: the last definitions keep being served.
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.True(await client.IsEnabledAsync("a"));
        await client.WaitForBackgroundRefreshAsync();
        Assert.True(await client.IsEnabledAsync("a"));
        Assert.Equal(Start, client.LastRefreshedAt);
    }

    [Fact]
    public async Task ExplicitRefresh_SurfacesHttpErrors()
    {
        using var client = Client(new FakeServer().Then(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<HttpRequestException>(() => client.RefreshAsync());
    }

    [Theory]
    [InlineData("", "k", "e", 30)]
    [InlineData("u", " ", "e", 30)]
    [InlineData("u", "k", "", 30)]
    [InlineData("u", "k", "e", 0)]
    public void Options_AreValidated(string url, string key, string env, int seconds) =>
        Assert.ThrowsAny<ArgumentException>(() =>
            new GrimoireFeatureFlagClient(
                new GrimoireFeatureFlagOptions
                {
                    BaseUrl = url,
                    ApiKey = key,
                    Environment = env,
                    RefreshInterval = TimeSpan.FromSeconds(seconds),
                }
            )
        );

    [Fact]
    public void Context_ConvertsValues()
    {
        var context = new FlagContext("u-1")
            .With("beta", true)
            .With("off", false)
            .With("ratio", 1.5)
            .With("count", 42L)
            .With("gone", "x")
            .With("gone", null);
        Assert.Equal("u-1", context[FlagContext.UserIdAttribute]);
        Assert.Equal(
            ("true", "false", "1.5", "42"),
            (context["beta"], context["off"], context["ratio"], context["count"])
        );
        Assert.False(context.ContainsKey("gone"));
        Assert.Equal(5, context.Count);
        Assert.Equal(5, context.Keys.Count());
        Assert.Contains("42", context.Values);
        Assert.True(context.TryGetValue("beta", out _));
        Assert.Empty(new FlagContext(""));
        Assert.Empty(FlagContext.Empty);
    }

    // ---- Microsoft.FeatureManagement ----

    private sealed class FixedContext(FlagContext context) : IFlagContextAccessor
    {
        public ValueTask<FlagContext> GetContextAsync() => ValueTask.FromResult(context);
    }

    private sealed class FixedTargeting(TargetingContext context) : ITargetingContextAccessor
    {
        public ValueTask<TargetingContext> GetContextAsync() => ValueTask.FromResult(context);
    }

    private const string TargetedJson = """
        {"environment":"production","flags":[
          {"key":"beta","type":"boolean","variants":[{"key":"on","value":true},{"key":"off","value":false}],
           "defaultVariant":"off","salt":"s","enabled":true,"offVariant":null,
           "rules":[{"conditions":[{"attribute":"userId","operator":"in","values":["u-1"]}],"serve":{"variant":"on"}},
                    {"conditions":[{"attribute":"groups","operator":"contains","values":["staff"]}],"serve":{"variant":"on"}}],
           "fallthrough":null,"version":3},
          {"key":"everyone","type":"boolean","variants":[{"key":"on","value":true},{"key":"off","value":false}],
           "defaultVariant":"off","salt":"s","enabled":true,"offVariant":null,"rules":[],"fallthrough":{"variant":"on"},"version":1}
        ]}
        """;

    private static IFeatureManager FeatureManager(Action<IServiceCollection>? extra = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGrimoireFeatureFlagClient>(
            Client(new FakeServer().Then(HttpStatusCode.OK, TargetedJson))
        );
        extra?.Invoke(services);
        services.AddGrimoireFeatureManagementCore();
        var provider = services.BuildServiceProvider();
        Assert.IsType<GrimoireFeatureDefinitionProvider>(
            provider.GetRequiredService<IFeatureDefinitionProvider>()
        );
        return provider.GetRequiredService<IFeatureManager>();
    }

    [Fact]
    public async Task FeatureManager_ListsAndEvaluatesFlags()
    {
        var manager = FeatureManager();
        var names = new List<string>();
        await foreach (var name in manager.GetFeatureNamesAsync())
            names.Add(name);
        Assert.Equal(["beta", "everyone"], names.Order());

        Assert.True(await manager.IsEnabledAsync("everyone"));
        Assert.False(await manager.IsEnabledAsync("beta"));
        Assert.False(await manager.IsEnabledAsync("missing"));
        Assert.True(await manager.IsEnabledAsync("beta", new FlagContext("u-1")));
        Assert.True(
            await manager.IsEnabledAsync<ITargetingContext>(
                "beta",
                new TargetingContext { UserId = "u-9", Groups = ["dev", "staff"] }
            )
        );
        Assert.False(
            await manager.IsEnabledAsync<ITargetingContext>(
                "beta",
                new TargetingContext { UserId = "u-9" }
            )
        );
    }

    [Fact]
    public async Task FeatureManager_UsesTheRegisteredContextAccessors()
    {
        var withFlagContext = FeatureManager(s =>
            s.AddSingleton<IFlagContextAccessor>(new FixedContext(new FlagContext("u-1")))
        );
        Assert.True(await withFlagContext.IsEnabledAsync("beta"));

        var withTargeting = FeatureManager(s =>
            s.AddSingleton<ITargetingContextAccessor>(
                new FixedTargeting(new TargetingContext { UserId = "u-1" })
            )
        );
        Assert.True(await withTargeting.IsEnabledAsync("beta"));
    }

    [Fact]
    public void Registration_ConfiguresAndValidatesTheClient()
    {
        var services = new ServiceCollection();
        services.AddGrimoireFeatureManagement(o =>
        {
            o.BaseUrl = "https://grimoire.test";
            o.ApiKey = "grm_key";
            o.Environment = "production";
        });
        using var provider = services.BuildServiceProvider();
        Assert.IsType<GrimoireFeatureFlagClient>(
            provider.GetRequiredService<IGrimoireFeatureFlagClient>()
        );

        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddGrimoireFeatureFlags(_ => { })
        );
        Assert.Equal(
            "groups",
            GrimoireFeatureFilter
                .FromTargeting(new TargetingContext { Groups = ["a", "b"] })
                .Keys.Single()
        );
        Assert.Empty(GrimoireFeatureFilter.FromTargeting(null));
    }
}
