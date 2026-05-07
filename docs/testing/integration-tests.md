---
title: Integration Tests
parent: Testing
nav_order: 2
---

# 🔗 Integration Tests
{: .no_toc }

Full HTTP-level tests that exercise the entire ASP.NET Core pipeline — middleware, controllers, validation, EF Core, encryption — against a real (in-process) SQLite database.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Running

```bash
dotnet test tests/Grimoire.IntegrationTests
```

**Expected output:** 50 tests, ~10 seconds.

---

## How it works

Integration tests use `WebApplicationFactory<Program>` from `Microsoft.AspNetCore.Mvc.Testing`. This starts the real ASP.NET Core application in-process, pointing at a unique temporary SQLite file per test class:

```csharp
public sealed class GrimoireWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _dbPath =
        Path.Combine(Path.GetTempPath(), $"grimoire-test-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = $"Data Source={_dbPath}",
                ["Management:AdminApiKey"]    = AdminKey,
                ["Encryption:MasterKey"]      = MasterKey,
                ["Cors:AllowedOrigins:0"]     = "http://localhost:5173"
            });
        });
    }
}
```

Key properties:

- Each test **class** gets its own factory (via `IClassFixture<GrimoireWebApplicationFactory>`)
- The factory creates a fresh temp SQLite file, runs EF Core migrations, then deletes the file on dispose
- Tests within a class run sequentially against the **shared** factory — they share state
- Tests across different classes run in parallel — they are fully isolated

---

## Test suites

### `AuthenticationTests` (6 tests)

Verifies that the middleware correctly guards endpoints:

| Test | Expected |
| :--- | :------- |
| Management endpoint without auth | `401` |
| Management endpoint with wrong Bearer | `401` |
| Management endpoint with correct Bearer | `200` |
| Consumer endpoint without X-Api-Key | `401` |
| Consumer endpoint with wrong key | `401` |
| Health endpoint (no auth) | `200` |

### `Management/ApplicationsTests` (9 tests)

| Test | Covers |
| :--- | :----- |
| List returns empty initially | — |
| Create returns 201 with slug and plainApiKey | Slug generation, key generation |
| Get by slug returns 200 | — |
| Get non-existent slug returns 404 | — |
| Update returns 200 | — |
| Delete returns 204, then 404 | Soft-delete |
| Rotate key returns new key; old key is invalid | Key rotation |
| `local` env is auto-seeded on create | Environment seeding |
| Duplicate name returns 409 | Unique slug enforcement |

### `Management/EnvironmentsTests` (6 tests)

Covers list, create, duplicate `409`, delete, and not-found scenarios for environments.

### `Management/SecretsTests` (8 tests)

Covers list, create, duplicate `409`, set values, nonexistent environment `404`, version history, multiple versions, and delete.

### `Management/ConfigurationsTests` (6 tests)

Covers list, create, duplicate `409`, update, delete, and nonexistent-environment `404`.

### `Consumer/ConsumerSecretsTests` (7 tests)

| Test | Scenario |
| :--- | :------- |
| `GetSecret_Returns200_WithDecryptedValue` | Full encrypt → store → decrypt cycle |
| `GetSecret_Returns404_WhenNoActiveVersion` | Secret exists but no value set |
| `GetSecret_Returns404_WhenSecretDoesNotExist` | Secret name doesn't exist |
| `GetSecret_Returns404_WhenEnvironmentNotFound` | Environment slug is wrong |
| `GetSecret_Returns404_WhenVersionDisabled` | Version has `IsEnabled = false` |
| `GetSecret_Returns404_WhenExpired` | Version has `ExpiresAt` in the past |
| `GetSecret_ReturnsLatestActiveVersion` | Two versions — returns `version: 2` |

### `Consumer/ConsumerConfigurationsTests` (5 tests)

Covers get-all with label, get-all empty, get-by-key `200`, get-by-key `404`, and unknown-env `404`.

---

## Test helpers

`TestHelpers.cs` contains shared utilities used across all management tests:

```csharp
// Unique names to prevent inter-test conflicts within a shared factory
public static string UniqueName(string prefix = "test") =>
    $"{prefix}-{Guid.NewGuid():N}"[..20];

// Create an application and return (slug, apiKey)
public static async Task<(string slug, string apiKey)> CreateApplicationAsync(
    HttpClient mgmtClient, string? name = null);

// Create an environment and return its slug
public static async Task<string> CreateEnvironmentAsync(
    HttpClient mgmtClient, string appSlug, string envName);

// Create a secret with a value set for an environment
public static async Task CreateSecretWithValueAsync(
    HttpClient mgmtClient, string appSlug, string secretName,
    string envSlug, string value);
```

---

## Why `WebApplicationFactory` not mocks?

Using `WebApplicationFactory` with a real SQLite database ensures that:

1. **Middleware is tested** — auth, exception handling, request logging all run
2. **EF Core queries are tested** — including subtle translation differences between in-memory and SQLite
3. **FluentValidation is tested** — through the full filter pipeline, not in isolation
4. **The integration between layers is tested** — controllers, repositories, encryption, all together

Mocking at any of these layers would produce tests that pass locally but miss real integration failures.
