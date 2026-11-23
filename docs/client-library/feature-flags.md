---
title: Feature Flags
parent: Client Library
nav_order: 3
---

# 🚩 Feature Flags
{: .no_toc }

`GrimoireFeatureFlagClient` evaluates [feature flags](../feature-flags/) **locally**. It downloads the definitions for one environment, keeps them in memory and runs the [evaluation spec](../feature-flags/evaluation-spec/):

- once loaded, checking a flag never waits on the network;
- your users' attributes never leave the process;
- if Grimoire is unreachable, the last definitions keep working.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

## Client

```csharp
using Grimoire.Consumer.FeatureFlags;

var flags = new GrimoireFeatureFlagClient(new GrimoireFeatureFlagOptions
{
    BaseUrl = "https://grimoire.example.com",
    ApiKey = builder.Configuration["Grimoire:ApiKey"]!,
    Environment = "production",
    RefreshInterval = TimeSpan.FromSeconds(30), // default
});

var user = new FlagContext("u-123")          // sets userId
    .With("email", "gui@straccini.com")
    .With("country", "IE")
    .With("appVersion", "2.1.0");

if (await flags.IsEnabledAsync("new-pdf-parser", user)) { … }

var layout = await flags.GetVariantAsync("checkout-redesign", user);        // "treatment"
var limits = await flags.GetValueAsync<Limits>("rate-limits", user, fallback: Limits.Default);

var result = await flags.EvaluateAsync("checkout-redesign", user);
// result.Variant, result.Value (JsonElement), result.Reason ("RULE_MATCH"), result.MatchedRuleIndex, result.Bucket
```

| Method | Returns |
| :----- | :------ |
| `IsEnabledAsync(key, context)` | `true` when the served value is the JSON `true`, which is a boolean flag's `on`. Unknown flags are off. |
| `GetVariantAsync(key, context)` | The variant key, or `null` for an unknown flag. |
| `GetValueAsync<T>(key, context, fallback)` | The variant's JSON value as `T`, or `fallback` when the flag is unknown or the value doesn't fit `T`. |
| `EvaluateAsync(key, context)` | A `FeatureFlagResult` with the variant, value, reason (`OFF`, `RULE_MATCH`, `FALLTHROUGH`, `MISSING_BUCKET_BY` or `FLAG_NOT_FOUND`), rule index and bucket. |
| `GetFlagKeysAsync()` | Every flag key in the cached definitions. |
| `RefreshAsync()` | Fetches now and returns whether anything changed. Unlike the other methods, it throws on HTTP errors. |

Create **one client per application and environment** and reuse it (register it as a singleton). It is thread-safe.

### Caching and refresh

- **First call:** loads the definitions. If Grimoire is unreachable, every flag reads as unknown (off or the fallback) and the next call tries again.
- **Later calls:** served from memory. Once the definitions are older than `RefreshInterval`, a call starts **one** background refresh and returns the cached result without waiting.
- **Conditional requests:** refreshes send the last `ETag` in `If-None-Match`, so unchanged flags cost a `304` with no body.
- **Failures:** a failed refresh (network error, HTTP error, timeout) keeps the last definitions in use.

### Context

`FlagContext` maps attribute names to strings. `With(name, bool)`, `With(name, double)` and `With(name, long)` store `true`/`false` and invariant numbers, as the spec compares them. `With(name, null)` removes an attribute. Rollouts bucket by `userId` unless a rule picks another attribute. A context without that attribute gets the flag's default variant (`MISSING_BUCKET_BY`), never a random one.

## Microsoft.FeatureManagement

Grimoire flags can back [`Microsoft.FeatureManagement`](https://learn.microsoft.com/azure/azure-app-configuration/feature-management-dotnet-reference), so `IFeatureManager`, `[FeatureGate]` and the `<feature>` tag helper work unchanged:

```csharp
builder.Services
    .AddGrimoireFeatureManagement(o =>
    {
        o.BaseUrl = "https://grimoire.example.com";
        o.ApiKey = builder.Configuration["Grimoire:ApiKey"]!;
        o.Environment = builder.Environment.EnvironmentName.ToLowerInvariant();
    });

// Optional: who the user is, for checks without a context ([FeatureGate], tag helpers).
builder.Services.AddSingleton<IFlagContextAccessor, HttpContextFlagContextAccessor>();
```

```csharp
public sealed class HttpContextFlagContextAccessor(IHttpContextAccessor http) : IFlagContextAccessor
{
    public ValueTask<FlagContext> GetContextAsync()
    {
        var user = http.HttpContext?.User;
        return ValueTask.FromResult(
            new FlagContext(user?.FindFirst("sub")?.Value)
                .With("email", user?.FindFirst("email")?.Value)
                .With("tenant", user?.FindFirst("tenant")?.Value));
    }
}
```

- Every Grimoire flag is a feature with the same name. It is **enabled** when it serves the JSON value `true`.
- `IsEnabledAsync(name, context)` accepts a `FlagContext`, or an `ITargetingContext`: its `UserId` becomes `userId`, and its `Groups` become `groups` (comma-separated, so target them with `contains`).
- Without a context, the filter uses a registered `IFlagContextAccessor`, then a registered `ITargetingContextAccessor`, and otherwise an empty context.
- **Variants:** Microsoft.FeatureManagement's variant allocation model differs from Grimoire's rules. For multivariate flags, call `GrimoireFeatureFlagClient` (`GetVariantAsync` / `GetValueAsync`) directly. `AddGrimoireFeatureManagement` registers it as `IGrimoireFeatureFlagClient`.

## Conformance

The client embeds the same evaluator as the server, built from the same source. Its tests replay every case in the shared [test vectors](https://github.com/guibranco/grimoire-api/blob/main/docs/feature-flags/test-vectors.json) through the client's own download-and-compile path.
