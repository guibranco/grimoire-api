---
title: 🔮 Grimoire API
layout: home
nav_order: 1
---

# 🔮 Grimoire API
{: .fs-9 }

Self-hosted Vault & Configuration Manager built with ASP.NET Core 10 and SQLite.
{: .fs-6 .fw-300 }

[Get started now]({{ site.baseurl }}/getting-started/){: .btn .btn-primary .fs-5 .mb-4 .mb-md-0 .mr-2 }
[View on GitHub](https://github.com/guibranco/grimoire-api){: .btn .fs-5 .mb-4 .mb-md-0 }

---

## What is Grimoire?

**Grimoire** is a lightweight, self-hosted alternative to Azure Key Vault and Azure App Configuration. It lets you:

- 🔐 **Store secrets** encrypted at rest with AES-256-GCM, versioned per environment
- ⚙️ **Manage configuration** key-value flags scoped per application and environment
- 🔑 **Issue API keys** per application, hash-stored and rotatable without downtime
- 🧩 **Consume natively** in .NET via a drop-in `IConfigurationSource` or typed HTTP client
- 🌍 **Support multiple environments** — `local`, `staging`, `production`, anything you need

---

## Quick overview

```text
┌─────────────────────────────────────────────────────┐
│                 Management plane                    │
│  Bearer token → CRUD for apps, envs, secrets, config│
└──────────────────────┬──────────────────────────────┘
                       │ stores encrypted at rest
                       ▼
              ┌─────────────────┐
              │   SQLite (EF)   │
              └────────┬────────┘
                       │ decrypts on read
                       ▼
┌─────────────────────────────────────────────────────┐
│                  Consumer plane                     │
│  X-Api-Key → read-only secrets + configuration      │
└─────────────────────────────────────────────────────┘
```

---

## Feature highlights

| 🏷️ Feature | 📝 Description |
| :--------- | :------------- |
| **Versioned secrets** | Every write creates a new version; old versions are kept for audit |
| **Time-windowed access** | `NotBefore` and `ExpiresAt` on each secret version |
| **Per-app API keys** | `grm_` prefixed hex keys, PBKDF2-hashed in the database |
| **AES-256-GCM encryption** | Nonce + tag + ciphertext stored as Base64; master key via HKDF |
| **Azure-compatible consumer** | Response shape matches Azure Key Vault / App Config for easy migration |
| **.NET `IConfigurationSource`** | Pull configs into `builder.Configuration` with one line |
| **Docker-first** | Multi-stage Dockerfile, `docker compose up`, `/health` endpoint |
| **100 tests** | 43 unit · 50 integration · 7 E2E (Testcontainers) |

---

## Technology stack

| Layer | Technology |
| :---- | :--------- |
| Runtime | .NET 10 / ASP.NET Core 10 |
| Database | SQLite via Entity Framework Core 10 |
| Encryption | AES-256-GCM + HKDF (System.Security.Cryptography) |
| Auth (management) | Bearer token middleware |
| Auth (consumer) | API key middleware + `IPasswordHasher<T>` |
| Validation | FluentValidation 11 |
| Mapping | Mapster 7 |
| Logging | Serilog (console + rolling file) |
| API docs | Swashbuckle / Swagger UI (two docs: management + consumer) |
| Container | Docker / Docker Compose |
| CI/CD | GitHub Actions + SonarCloud |
| Docs | Jekyll + Just the Docs (this site) |

---

## Getting started

Head to [Installation]({{ site.baseurl }}/getting-started/installation/) to run Grimoire in under five minutes, or read [Architecture]({{ site.baseurl }}/architecture/) to understand how everything fits together.
