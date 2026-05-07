---
title: Overview
parent: Architecture
nav_order: 1
---

# 🗺️ Architecture Overview
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Solution structure

```text
grimoire-api/
├── src/
│   ├── Grimoire.Core            ← Domain layer
│   ├── Grimoire.Infrastructure  ← Data + services layer
│   ├── Grimoire.Api             ← HTTP API layer
│   └── Grimoire.Consumer        ← Client library
└── tests/
    ├── Grimoire.Tests            ← Unit tests
    ├── Grimoire.IntegrationTests ← Integration tests
    └── Grimoire.E2eTests         ← End-to-end tests
```

---

## Project dependency graph

```text
Grimoire.Api
  ├── Grimoire.Core           (entities, interfaces)
  └── Grimoire.Infrastructure (EF Core, encryption, repositories)
        └── Grimoire.Core

Grimoire.Consumer            (standalone — no dependency on Api or Infrastructure)
```

`Grimoire.Consumer` is intentionally isolated. It communicates with the API over HTTP and has no compile-time coupling to server-side code.

---

## Layer responsibilities

### Grimoire.Core

The innermost layer. Contains only:

- **Entities** — `Application`, `AppEnvironment`, `Secret`, `SecretVersion`, `ConfigurationEntry`
- **Interfaces** — `IEncryptionService` and four repository interfaces (`IApplicationRepository`, `IEnvironmentRepository`, `ISecretRepository`, `IConfigurationRepository`)

No framework references. No EF Core. No dependency injection.

### Grimoire.Infrastructure

Implements the Core interfaces:

- **`GrimoireDbContext`** — EF Core `DbContext` with Fluent API entity configurations, global query filters for soft-delete, and automatic migration on startup
- **Repositories** — `ApplicationRepository`, `EnvironmentRepository`, `SecretRepository`, `ConfigurationRepository`
- **`AesGcmEncryptionService`** — AES-256-GCM encryption with HKDF key derivation
- **`SlugService`** — Converts application names to URL-safe slugs
- **`GrimoireDbContextFactory`** — Design-time EF Core factory for running migrations from the CLI

### Grimoire.Api

The ASP.NET Core 10 host:

- **Controllers** split into two namespaces:
  - `Management/*` — full CRUD behind Bearer token auth
  - `Consumer/*` — read-only behind API key auth
- **Middleware** — `AdminApiKeyMiddleware` and `ConsumerApiKeyMiddleware`
- **Validators** — FluentValidation rules for all request DTOs
- **Swagger** — two separate docs (`management` and `consumer`)
- **Program.cs** — DI wiring, middleware pipeline, health check, migration on startup

### Grimoire.Consumer

A standalone .NET library for consuming the API:

- **`GrimoireSecretClient`** — typed HTTP client for individual secret reads
- **`GrimoireConfigurationClient`** — implements `IConfigurationProvider` + `IConfigurationSource`
- **`GrimoireConfigurationExtensions`** — `AddGrimoire()` extension on `IConfigurationBuilder`

---

## Request pipeline

Every HTTP request flows through:

```text
Request
  │
  ├─► Serilog request logging
  ├─► Exception handler (ProblemDetails)
  ├─► CORS
  ├─► AdminApiKeyMiddleware
  │     (validates Bearer token for /api/management/*)
  ├─► ConsumerApiKeyMiddleware
  │     (validates X-Api-Key for /api/consumer/*)
  │     (stores matched Application in HttpContext.Items)
  └─► Controllers
        ├─► Management controllers (FluentValidation, repository calls)
        └─► Consumer controllers (repository + decryption)
```

### Authentication short-circuit

Both middleware components return `401 Unauthorized` immediately if:
- The required header is missing
- The token/key does not match

They do not call `next()` in the rejection path, so no controller code runs.

### Consumer identity propagation

`ConsumerApiKeyMiddleware` resolves the `Application` entity from the database, verifies the API key hash, and stores the entity in `HttpContext.Items["GrimoireApplication"]`. Consumer controllers read it from there rather than re-querying the database.

---

## Database migrations

EF Core migrations are stored in `src/Grimoire.Infrastructure/Persistence/Migrations/`.

On startup, `Program.cs` runs:

```csharp
using var scope = app.Services.CreateScope();
var db = scope.ServiceProvider.GetRequiredService<GrimoireDbContext>();
db.Database.Migrate();
```

This applies any pending migrations automatically. The SQLite file is created on first run.

---

## Swagger UI

The API exposes two separate Swagger documents at runtime:

| Doc | URL | Audience |
| :-- | :-- | :------- |
| Management | `/swagger/management/swagger.json` | Administrators |
| Consumer | `/swagger/consumer/swagger.json` | Application developers |

The Swagger UI is available at `/swagger` in `Development` environment only.
