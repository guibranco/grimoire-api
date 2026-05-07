---
title: Secret Client
parent: Client Library
nav_order: 2
---

# 🔐 Secret Client
{: .no_toc }

`GrimoireSecretClient` is a typed HTTP client for fetching individual secrets at runtime with full access to version metadata.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Construction

```csharp
var client = new GrimoireSecretClient(
    baseUrl:     "http://localhost:8080",
    apiKey:      "grm_your_api_key_here",
    environment: "production"
);
```

The client creates an internal `HttpClient` with `BaseAddress` and `X-Api-Key` pre-set. The environment is fixed at construction time.

---

## Fetching a secret

```csharp
GrimoireSecret secret = await client.GetSecretAsync("database-password");

Console.WriteLine(secret.Name);                    // "database-password"
Console.WriteLine(secret.Value);                   // "super-secret-db-pass"
Console.WriteLine(secret.Properties.Enabled);      // true
Console.WriteLine(secret.Properties.Version);      // 2
Console.WriteLine(secret.Properties.ExpiresOn);    // null or DateTimeOffset
Console.WriteLine(secret.Properties.NotBefore);    // null or DateTimeOffset
Console.WriteLine(secret.Properties.CreatedOn);    // DateTimeOffset
```

### `GrimoireSecret` model

```csharp
public record GrimoireSecret(
    string Name,
    string Value,
    SecretProperties Properties
);

public record SecretProperties(
    bool              Enabled,
    DateTimeOffset?   ExpiresOn,
    DateTimeOffset?   NotBefore,
    int               Version,
    DateTimeOffset    CreatedOn,
    DateTimeOffset?   UpdatedOn
);
```

---

## Error handling

`GetSecretAsync` calls `EnsureSuccessStatusCode()` internally. Handle `HttpRequestException` for:

- **`404 Not Found`** — secret doesn't exist or has no active version
- **`401 Unauthorized`** — invalid API key
- **Network errors** — Grimoire API unreachable

```csharp
try
{
    var secret = await client.GetSecretAsync("database-password");
    // use secret.Value
}
catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
{
    // Secret not found or no active version — fall back to default
    connectionString = defaultConnectionString;
}
```

---

## Dependency injection

For use in ASP.NET Core, register the client as a singleton:

```csharp
builder.Services.AddSingleton(sp =>
    new GrimoireSecretClient(
        builder.Configuration["Grimoire:BaseUrl"]!,
        builder.Configuration["Grimoire:ApiKey"]!,
        builder.Configuration["Grimoire:Environment"] ?? "production"
    )
);
```

Then inject it normally:

```csharp
public class MyService(GrimoireSecretClient grimoire)
{
    public async Task DoWorkAsync(CancellationToken ct)
    {
        var key = await grimoire.GetSecretAsync("encryption-key", ct);
        // ...
    }
}
```

---

## Cancellation

All async methods accept an optional `CancellationToken`:

```csharp
using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
var secret = await client.GetSecretAsync("database-password", cts.Token);
```

---

## When to use `GrimoireSecretClient` vs `GrimoireConfigurationClient`

| Scenario | Recommended |
| :------- | :---------- |
| Need configs available as `IConfiguration` on startup | `GrimoireConfigurationClient` (via `AddGrimoire`) |
| Need a secret only when a specific operation runs | `GrimoireSecretClient` |
| Need `Properties.Version` or expiry metadata | `GrimoireSecretClient` |
| Accessing secrets in background jobs or workers | `GrimoireSecretClient` |
| Using `IOptions<T>` binding | `GrimoireConfigurationClient` |
