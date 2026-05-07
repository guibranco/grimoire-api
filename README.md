# 🔮 Grimoire API

> **Self-hosted Vault & Configuration Manager** — securely store secrets and feature flags, then consume them at runtime via a Key Vault-compatible REST API or the native .NET client library.

[![Build & Test](https://github.com/guibranco/grimoire-api/actions/workflows/build.yml/badge.svg)](https://github.com/guibranco/grimoire-api/actions/workflows/build.yml)
[![Quality Gate Status](https://sonarcloud.io/api/project_badges/measure?project=guibranco_grimoire-api&metric=alert_status)](https://sonarcloud.io/summary/new_code?id=guibranco_grimoire-api)
[![Coverage](https://sonarcloud.io/api/project_badges/measure?project=guibranco_grimoire-api&metric=coverage)](https://sonarcloud.io/summary/new_code?id=guibranco_grimoire-api)
[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Docker](https://img.shields.io/badge/Docker-ready-2496ED?logo=docker)](https://www.docker.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

---

## ✨ Features

| Feature | Details |
| ------- | ------- |
| 🔐 **Secret management** | Versioned, per-environment secrets with enable/disable, expiry, and `NotBefore` support |
| ⚙️ **Configuration flags** | Key-value configuration entries scoped per application and environment |
| 🔑 **API key auth** | Per-application hashed API keys; rotate without downtime |
| 🛡️ **AES-256-GCM encryption** | All secret values encrypted at rest with HKDF-derived keys |
| 🌍 **Multi-environment** | Each application can have unlimited named environments (e.g. `local`, `staging`, `production`) |
| 🧩 **.NET client library** | Drop-in `IConfigurationSource` — pull secrets and flags directly into `IConfiguration` |
| 🐳 **Docker-first** | Single `docker compose up` to run the entire stack |
| 📊 **Swagger UI** | Separate Management and Consumer API docs at `/swagger` |

---

## 🏗️ Architecture

```text
grimoire-api/
├── 📦 src/
│   ├── Grimoire.Core            # Domain entities & repository interfaces
│   ├── Grimoire.Infrastructure  # EF Core + SQLite, AES-GCM encryption, slug service
│   ├── Grimoire.Api             # ASP.NET Core 10 — Management & Consumer REST APIs
│   └── Grimoire.Consumer        # .NET client library (NuGet-ready)
└── 🧪 tests/
    ├── Grimoire.Tests            # Unit tests (validators, slug service)
    ├── Grimoire.IntegrationTests # WebApplicationFactory integration tests
    └── Grimoire.E2eTests         # Testcontainers end-to-end tests
```

### 🔄 Request flow

```text
Client app                Grimoire API                   SQLite DB
──────────                ─────────────                  ─────────
X-Api-Key ──► ConsumerApiKeyMiddleware ──► verify hash
                          │
              ConsumerSecretsController ──► SecretRepository ──► DB
                          │                        │
              AES-256-GCM decrypt ◄─────── EncryptedValue
                          │
              ◄── { name, value, properties }
```

---

## 🚀 Quick Start

### 🐳 Option A — Docker Compose (recommended)

```bash
# 1. Clone
git clone https://github.com/guibranco/grimoire-api.git
cd grimoire-api

# 2. Set your secrets (edit the compose file or use env vars)
export Management__AdminApiKey="your-admin-key"
export Encryption__MasterKey="your-32-char-minimum-master-key!!"

# 3. Run
docker compose up -d

# ✅ API is now available at http://localhost:8080
# 📖 Swagger UI: http://localhost:8080/swagger
```

### 💻 Option B — dotnet run

```bash
dotnet run --project src/Grimoire.Api \
  --Management:AdminApiKey="your-admin-key" \
  --Encryption:MasterKey="your-32-char-minimum-master-key!!"
```

---

## 📡 API Overview

All **management** endpoints require a `Bearer` token matching `Management:AdminApiKey`.  
All **consumer** endpoints require an `X-Api-Key` header with a valid application API key.

### 🔧 Management API

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| `GET` | `/api/management/applications` | List all applications |
| `POST` | `/api/management/applications` | Create application (auto-seeds `local` env) |
| `GET` | `/api/management/applications/{slug}` | Get application |
| `PUT` | `/api/management/applications/{slug}` | Update application |
| `DELETE` | `/api/management/applications/{slug}` | Soft-delete application |
| `POST` | `/api/management/applications/{slug}/rotate-key` | 🔄 Rotate API key |
| `GET` | `/api/management/applications/{slug}/environments` | List environments |
| `POST` | `/api/management/applications/{slug}/environments` | Create environment |
| `DELETE` | `/api/management/applications/{slug}/environments/{envSlug}` | Delete environment |
| `GET` | `/api/management/applications/{slug}/secrets` | List secrets |
| `POST` | `/api/management/applications/{slug}/secrets` | Create secret |
| `GET` | `/api/management/applications/{slug}/secrets/{name}` | Get secret metadata |
| `POST` | `/api/management/applications/{slug}/secrets/{name}/values` | Set secret value(s) |
| `GET` | `/api/management/applications/{slug}/secrets/{name}/versions/{environmentSlug}` | List versions |
| `DELETE` | `/api/management/applications/{slug}/secrets/{name}` | Delete secret |
| `GET` | `/api/management/applications/{slug}/configurations` | List configurations |
| `POST` | `/api/management/applications/{slug}/configurations` | Create configuration |
| `PUT` | `/api/management/applications/{slug}/configurations/{env}/{key}` | Update configuration |
| `DELETE` | `/api/management/applications/{slug}/configurations/{env}/{key}` | Delete configuration |

### 🔌 Consumer API

| Method | Endpoint | Description |
| ------ | -------- | ----------- |
| `GET` | `/api/consumer/secrets/{name}?environment={env}` | 🔓 Get decrypted secret value |
| `GET` | `/api/consumer/configurations?environment={env}` | Get all configurations |
| `GET` | `/api/consumer/configurations/{key}?environment={env}` | Get single configuration |

---

## 🧩 .NET Client Library

Install the `Grimoire.Consumer` package (or reference the project) and pull secrets directly into your `IConfiguration`:

```csharp
// Program.cs — drop-in IConfigurationSource
var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddGrimoire(
    baseUrl:     "http://localhost:8080",
    apiKey:      "grm_your_api_key_here",
    environment: "production"
);

// Use IConfiguration as normal — secrets and configs are transparently available
var dbPassword = builder.Configuration["db-password"];
var featureEnabled = builder.Configuration["Feature:EnableDarkMode"];
```

Or use the typed client directly:

```csharp
var client = new GrimoireSecretClient(
    baseUrl:     "http://localhost:8080",
    apiKey:      "grm_your_api_key_here",
    environment: "production"
);

var secret = await client.GetSecretAsync("db-password");
Console.WriteLine(secret.Value);                  // 🔓 plain text
Console.WriteLine(secret.Properties.Enabled);     // true/false
Console.WriteLine(secret.Properties.Version);     // 2
```

---

## ⚙️ Configuration Reference

Settings can be provided via `appsettings.json`, environment variables (use `__` as separator), or CLI args.

| Setting | Required | Default | Description |
| ------- | :------: | ------- | ----------- |
| `ConnectionStrings:Default` | ✅ | `grimoire.db` | SQLite connection string |
| `Management:AdminApiKey` | ✅ | *(none)* | Bearer token for Management API |
| `Encryption:MasterKey` | ✅ | *(none)* | Master key for HKDF derivation (≥ 32 chars) |
| `Cors:AllowedOrigins` | ➖ | `[]` | Allowed CORS origins |
| `Serilog:MinimumLevel:Default` | ➖ | `Information` | Log level |

> ⚠️ **Security note:** Never commit real values for `AdminApiKey` or `MasterKey`.  
> Use environment variables, Docker secrets, or a secrets manager in production.

---

## 🔒 Security Model

```text
┌──────────────────────────────────────────────────────────┐
│                    🔐 Secret at rest                     │
│                                                          │
│  MasterKey ──► HKDF-SHA256 ──► 256-bit AES key           │
│  plaintext ──► AES-256-GCM ──► nonce(12) + tag(16) + 🔒  │
│                (stored in SQLite as Base64)               │
└──────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────┐
│                    🔑 API key lifecycle                  │
│                                                          │
│  GenerateApiKey() → grm_{40 hex chars}                   │
│  HashPassword()   → PBKDF2 hash stored in DB             │
│  VerifyHash()     → compared on every consumer request   │
│  RotateKey()      → new key issued, old hash replaced    │
└──────────────────────────────────────────────────────────┘
```

---

## 🧪 Running Tests

```bash
# 🔬 Unit + integration tests
dotnet test --filter "FullyQualifiedName!~E2eTests"

# 🐳 E2E tests (requires Docker)
docker build -t grimoire-api:e2e .
GRIMOIRE_TEST_IMAGE=grimoire-api:e2e dotnet test tests/Grimoire.E2eTests

# 📊 With OpenCover coverage (for SonarCloud)
dotnet test --filter "FullyQualifiedName!~E2eTests" \
  --collect:"XPlat Code Coverage" \
  --results-directory ./coverage \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=opencover
```

| Test Suite | Count | Type |
| ---------- | :---: | ---- |
| 🔬 `Grimoire.Tests` | **43** | Unit — validators, slug service |
| 🔗 `Grimoire.IntegrationTests` | **50** | Integration — full HTTP via `WebApplicationFactory` |
| 🐳 `Grimoire.E2eTests` | **7** | End-to-end — real Docker container via Testcontainers |

---

## 🛠️ Development

### 📋 Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for E2E tests)

### 🗃️ Database Migrations

```bash
# Add a new migration
dotnet ef migrations add <MigrationName> \
  --project src/Grimoire.Infrastructure \
  --startup-project src/Grimoire.Api

# Apply to local DB
dotnet ef database update \
  --project src/Grimoire.Infrastructure \
  --startup-project src/Grimoire.Api
```

### 🗂️ Project References

```text
Grimoire.Api
  ├── Grimoire.Core
  └── Grimoire.Infrastructure
        └── Grimoire.Core

Grimoire.Consumer  (standalone, no Api dependency)
```

---

## 📁 Solution Structure

```text
src/Grimoire.Core/
  Entities/           Application, AppEnvironment, Secret, SecretVersion, ConfigurationEntry
  Interfaces/         IEncryptionService + 4 repository interfaces

src/Grimoire.Infrastructure/
  Persistence/        GrimoireDbContext, EF entity configs, repositories, migrations
  Services/           AesGcmEncryptionService, SlugService

src/Grimoire.Api/
  Controllers/        Management/* and Consumer/*
  DTOs/               Request/response records (CreateApplicationRequest, etc.)
  Middleware/         AdminApiKeyMiddleware, ConsumerApiKeyMiddleware
  Validators/         FluentValidation rules for all request types

src/Grimoire.Consumer/
  GrimoireSecretClient.cs             Typed HTTP client
  GrimoireConfigurationClient.cs      IConfigurationSource / IConfigurationProvider
  GrimoireConfigurationExtensions.cs  AddGrimoire() builder extension
```

---

## 🤝 Contributing

1. 🍴 Fork the repository
2. 🌿 Create a feature branch: `git checkout -b feature/my-feature`
3. ✅ Commit your changes and ensure all tests pass
4. 📬 Push and open a pull request against `main`

All PRs are automatically analyzed by SonarCloud and must pass the CI pipeline (build + unit + integration tests). E2E tests run as a separate Docker job.

---

## 📄 License

MIT © [Guilherme Branco Stracini](https://github.com/guibranco)

---

Made with ❤️ and a sprinkle of 🔮 magic
