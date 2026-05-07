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
2. `Load()` calls `GET /api/consumer/configurations?environment={env}`
3. Receives a flat list of `{ key, value }` pairs
4. Populates the internal `Data` dictionary (inherited from `ConfigurationProvider`)

The configuration is loaded **once at startup**. It is not reloaded dynamically. If you need live config refresh, restart the application or implement a custom `IConfigurationProvider` that polls on a timer.

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

// Get all keys as a dictionary
IReadOnlyDictionary<string, string> all = await client.GetAllAsync();
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
