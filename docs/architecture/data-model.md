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

GlobalConfigurationEntry (N)   ← not owned by any application
  └── EnvironmentSlug          ← "*" or an environment slug, matched by name

GlobalSecret (N)
  └── GlobalSecretVersion (N)  ← one per (GlobalSecret × EnvironmentSlug × write)
```

All application-scoped entities reference `Application` via `ApplicationId`. Global entities have no application: they apply to every application with `InheritGlobals = true` (see [Global values and precedence](#global-values-and-precedence)). Global query filters on `Application` propagate soft-delete automatically: when an application is soft-deleted, its secrets and configs become invisible to all queries.

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
| `InheritGlobals` | `bool` | When `true` (default), global configuration and secrets apply to this application |
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
| `Value` | `string` | Plain text — not encrypted. Minified JSON for `Array` and `Json` |
| `ValueType` | `ConfigurationValueType` | `String` (default), `Multiline`, `Array` or `Json`; stored as text |
| `Description` | `string?` | Optional |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

{: .warning }
Configuration entries are stored **unencrypted**. Do not store sensitive values as configuration entries — use secrets instead.

### GlobalConfigurationEntry

A configuration entry defined once and inherited by every application with `InheritGlobals = true`. Like `ConfigurationEntry`, it is stored unencrypted.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `EnvironmentSlug` | `string` | `*` (all environments, the default) or an environment slug such as `production` |
| `Key` | `string` | Configuration key (up to 500 chars) |
| `Value` | `string` | Plain text — not encrypted. Minified JSON for `Array` and `Json` |
| `ValueType` | `ConfigurationValueType` | Same as `ConfigurationEntry.ValueType` |
| `Description` | `string?` | Optional (up to 1000 chars) |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

{: .note }
`AppEnvironment` is per application, so globals cannot reference an environment by id. They target environments **by slug**: `production` matches every application's `production` environment. `*` is used instead of `NULL` for "all environments" so the unique index on `(EnvironmentSlug, Key)` holds (SQLite treats `NULL`s as distinct in unique indexes).

### GlobalSecret

A named secret slot shared by every application with `InheritGlobals = true`.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `Name` | `string` | Unique identifier (up to 200 chars) |
| `Description` | `string?` | Optional |
| `CreatedAt` | `DateTimeOffset` | — |
| `UpdatedAt` | `DateTimeOffset` | — |

### GlobalSecretVersion

Same as `SecretVersion`, but targets an environment by slug. Values are encrypted with the same AES-256-GCM `IEncryptionService` as application secrets.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `GlobalSecretId` | `Guid` | FK → GlobalSecret (cascade delete) |
| `EnvironmentSlug` | `string` | `*` or an environment slug |
| `EncryptedValue` | `string` | Base64-encoded `nonce(12) + tag(16) + ciphertext` |
| `IsEnabled` | `bool` | — |
| `ExpiresAt` | `DateTimeOffset?` | — |
| `NotBefore` | `DateTimeOffset?` | — |
| `Version` | `int` | Monotonically increasing per (GlobalSecret, EnvironmentSlug) |
| `CreatedAt` | `DateTimeOffset` | — |

The active global version for an environment is the latest active version for that slug; if there is none, the latest active version for `*`.

---

## Global values and precedence

When a consumer reads configuration or a secret, Grimoire resolves it in this order (highest wins):

1. The application's own value for that environment
2. A global value for the environment's slug (e.g. `production`)
3. A global value for `*`

Globals only apply when the application has `InheritGlobals = true`. Applications with `InheritGlobals = false` see only their own values.

**Configuration** keys are merged case-insensitively (as .NET `IConfiguration` does), so an application key overrides a global key that differs only in case.

**Secrets** fall back to a global secret **only when the application has no secret with that name**. If the application defines the secret but it has no active version for the environment (disabled, expired, not yet valid, or never set), the consumer gets `404`. An explicit application-level disable is never silently replaced by a global value.

The merge lives in `IEffectiveConfigurationService` (`Grimoire.Core`), which both the Consumer API and the Management API's `/effective` endpoints use.

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
| `GlobalConfigurationEntry` | `(EnvironmentSlug, Key)` |
| `GlobalSecret` | `Name` |

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
