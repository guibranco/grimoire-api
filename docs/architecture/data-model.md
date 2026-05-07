---
title: Data Model
parent: Architecture
nav_order: 2
---

# 🗃️ Data Model
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Entity relationship diagram

```text
Application (1)
  │
  ├── AppEnvironment (N)   ← named scopes: "local", "staging", "production"
  │
  ├── Secret (N)
  │     └── SecretVersion (N)  ← one per (Secret × AppEnvironment × write)
  │           └── EnvironmentId → AppEnvironment
  │
  └── ConfigurationEntry (N)
        └── EnvironmentId → AppEnvironment
```

All child entities reference `Application` via `ApplicationId`. Global query filters on `Application` propagate soft-delete automatically: when an application is soft-deleted, its secrets and configs become invisible to all queries.

---

## Entities

### Application

The top-level owner of all other entities. Represents a single service or workload.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `Name` | `string` | Display name (up to 200 chars) |
| `Slug` | `string` | URL-safe, unique identifier generated from `Name` |
| `Description` | `string?` | Optional description |
| `ApiKeyHash` | `string` | PBKDF2 hash of the plain API key |
| `IsDeleted` | `bool` | Soft-delete flag |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

### AppEnvironment

A named deployment environment (e.g. `local`, `staging`, `production`) scoped to an application.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | FK → Application |
| `Name` | `string` | Display name |
| `Slug` | `string` | URL-safe identifier |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

{: .note }
A **`local`** environment is automatically seeded when an application is created.

### Secret

A named secret slot. Does not hold a value itself — values live in `SecretVersion`.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | FK → Application |
| `Name` | `string` | Identifier used in API calls and client reads |
| `Description` | `string?` | Optional |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

### SecretVersion

An immutable encrypted value for a `(Secret, AppEnvironment)` pair. Every write creates a new version; old versions are never overwritten.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `SecretId` | `Guid` | FK → Secret |
| `EnvironmentId` | `Guid` | FK → AppEnvironment |
| `EncryptedValue` | `string` | Base64-encoded `nonce(12) + tag(16) + ciphertext` |
| `IsEnabled` | `bool` | Disabled versions are skipped on consumer read |
| `ExpiresAt` | `DateTimeOffset?` | Version is inactive after this timestamp |
| `NotBefore` | `DateTimeOffset?` | Version is inactive before this timestamp |
| `Version` | `int` | Monotonically increasing per (Secret, Environment) |
| `CreatedAt` | `DateTimeOffset` | — |

The consumer API returns the highest-version record where:
- `IsEnabled = true`
- `NotBefore IS NULL OR NotBefore <= now`
- `ExpiresAt IS NULL OR ExpiresAt >= now`

### ConfigurationEntry

A plain-text key-value pair for feature flags and non-secret settings.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | FK → Application |
| `EnvironmentId` | `Guid` | FK → AppEnvironment |
| `Key` | `string` | Configuration key (e.g. `Feature:DarkMode`, `MaxRetries`) |
| `Value` | `string` | Plain text — not encrypted |
| `Description` | `string?` | Optional |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

{: .warning }
Configuration entries are stored **unencrypted**. Do not store sensitive values as configuration entries — use secrets instead.

---

## EF Core configuration highlights

### Global query filter (soft delete)

```csharp
// ApplicationConfiguration.cs
builder.HasQueryFilter(a => !a.IsDeleted);
```

Because `Application` has a global filter, EF Core automatically appends `WHERE IsDeleted = 0` to every query involving `Application`. All child entities that navigate back to `Application` are similarly filtered.

### Unique indexes

| Entity | Unique on |
| :----- | :-------- |
| `Application` | `Slug` |
| `AppEnvironment` | `(ApplicationId, Slug)` |
| `Secret` | `(ApplicationId, Name)` |
| `ConfigurationEntry` | `(ApplicationId, EnvironmentId, Key)` |

These are enforced at the database level and surface as `409 Conflict` responses in the API.

### SlugService

Application names are converted to slugs using `SlugService.Generate()`:

- Lowercase
- Non-alphanumeric characters removed (dashes preserved)
- Multiple consecutive dashes collapsed
- Leading and trailing dashes removed

```
"My New App"    → "my-new-app"
"Hello! World"  → "hello-world"
"A---B---C"     → "a-b-c"
```
