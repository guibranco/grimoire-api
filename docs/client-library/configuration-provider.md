---
title: Configuration Provider
parent: Client Library
nav_order: 1
---

# ⚙️ Configuration Provider
{: .no_toc }

`GrimoireConfigurationClient` implements both `IConfigurationSource` and `IConfigurationProvider`, integrating Grimoire seamlessly into the standard .NET configuration pipeline.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Quick setup

```csharp
// Program.cs
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddGrimoire(
    baseUrl:     "http://localhost:8080",
    apiKey:      "grm_your_api_key_here",
    environment: "production"
);
```

That's it. All configuration entries for the `production` environment are loaded into `builder.Configuration` and available through the standard `IConfiguration` interface.

---

## How it works

`AddGrimoire()` calls `builder.Configuration.Add(new GrimoireConfigurationClient(...))`, which:

1. When `IConfiguration` is first built, calls `Load()` synchronously
2. `Load()` calls `GET /consumer/configurations?environment={env}`
3. Receives a flat list of `{ key, value }` pairs
4. Populates the internal `Data` dictionary (inherited from `ConfigurationProvider`)

By default the configuration is loaded **once at startup**. To pick up changes without a restart, turn on refresh (below).

---

## Reloading on changes

```csharp
builder.Configuration.AddGrimoire(o => o
    .Connect(baseUrl, apiKey, "production")
    .ConfigureRefresh(r => r.SetRefreshInterval(TimeSpan.FromSeconds(30))));

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
// IOptionsMonitor<SmtpOptions> now sees new values after a publish.
```

With refresh on, the provider polls the **sentinel keys** `Sentinel` and `Global:Sentinel` on a background timer (30 s plus jitter by default), using `If-None-Match`, so an unchanged check costs a `304`. When either sentinel changes, the provider reloads every value, swaps it in atomically and calls `OnReload()`. That fires `IConfiguration` reload tokens and updates `IOptionsMonitor<T>` / `IOptionsSnapshot<T>`. `IOptions<T>` stays a startup snapshot, as usual in .NET.

Grimoire bumps the sentinels on every change while **auto-publish** is on (the default), or only when you publish changes while it is off. See the [refresh spec](refresh-spec/).

- **Failures:** a failed check keeps the current values and backs off exponentially, up to 5 minutes.
- **Watching other keys:** `r.Register("Feature:Banner")` watches one key and updates only that key when it changes. `r.Register("MyKey", refreshAll: true)` reloads everything when it changes. Registering any key replaces the default sentinel pair, so register `Sentinel` and `Global:Sentinel` (with `refreshAll: true`) if you still want them.
- **Check now:** cast the provider (`((IConfigurationRoot)config).Providers.OfType<GrimoireConfigurationClient>()`) and call `TryRefreshAsync()`.
- **Secrets:** `GrimoireSecretClient` doesn't cache, so it always returns current values.

---

## Using the loaded configuration

```csharp
// After AddGrimoire(), use IConfiguration as normal:
var dbHost    = builder.Configuration["Database:Host"];
var maxRetry  = builder.Configuration.GetValue<int>("MaxRetries");
var featureOn = builder.Configuration.GetValue<bool>("Feature:DarkMode");

// Works with IOptions<T> too
builder.Services.Configure<DatabaseOptions>(
    builder.Configuration.GetSection("Database")
);
```

---

## Colon-delimited keys

Grimoire keys map directly to the IConfiguration key hierarchy:

| Grimoire key | .NET access |
| :----------- | :---------- |
| `Database:Host` | `config["Database:Host"]` |
| `Database:Port` | `config.GetSection("Database")["Port"]` |
| `Feature:DarkMode` | `config.GetValue<bool>("Feature:DarkMode")` |
| `MaxRetries` | `config.GetValue<int>("MaxRetries")` |

---

## Arrays and JSON values

Entries with the `array` or `json` [value type](../api-reference/management-api.md#value-types) are flattened the same way `appsettings.json` is, so they bind with the usual APIs:

| Grimoire entry | Loaded as |
| :------------- | :-------- |
| `AllowedIps` (array) = `["10.0.0.1","10.0.0.2"]` | `AllowedIps:0` = `10.0.0.1`, `AllowedIps:1` = `10.0.0.2` |
| `Smtp` (json) = `{"host":"mail","port":25}` | `Smtp:host` = `mail`, `Smtp:port` = `25` |

```csharp
string[] allowedIps = builder.Configuration.GetSection("AllowedIps").Get<string[]>() ?? [];

builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection("Smtp"));
```

The parent key itself (`AllowedIps`) has no value once flattened, as with a JSON file. `string` and `multiline` values are never parsed, even if they look like JSON.

---

## Priority

`AddGrimoire()` adds the Grimoire source at the **end** of the configuration pipeline. This means:

- Grimoire values override `appsettings.json` and `appsettings.{Environment}.json`
- **Environment variables** and **command-line arguments** (added later) can still override Grimoire

To change priority, call `AddGrimoire()` earlier or later in the chain.

---

## Async access

The `GrimoireConfigurationClient` also exposes async methods for use outside the configuration pipeline:

```csharp
// Inject or instantiate the client directly
var client = new GrimoireConfigurationClient(baseUrl, apiKey, environment);

// Get a single key
string? value = await client.GetAsync("Feature:DarkMode");

// Get all keys as a dictionary (array and JSON values as raw JSON text)
IReadOnlyDictionary<string, string> all = await client.GetAllAsync();

// Deserialise an array or JSON value
string[]? allowedIps = await client.GetAsync<string[]>("AllowedIps");
SmtpOptions? smtp = await client.GetAsync<SmtpOptions>("Smtp");
```

---

## Error handling

If the Grimoire API is unavailable at startup, `Load()` will throw an `HttpRequestException`, which will prevent the application from starting. This is intentional — a service that cannot read its configuration should not start.

To tolerate failures (e.g. during local development without Grimoire), wrap the call:

```csharp
try
{
    builder.Configuration.AddGrimoire(baseUrl, apiKey, environment);
}
catch (HttpRequestException)
{
    // Grimoire unavailable — fall back to local config only
    Console.Error.WriteLine("Warning: Grimoire unreachable, using local config");
}
```
