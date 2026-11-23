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
  ├── AccessToken (N)      ← Consumer API tokens, limited to environment slugs and scopes
  ├── TrustPolicy (N)      ← GitHub OIDC: which runs may get a short-lived AccessToken
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
| `RotationIntervalDays` | `int?` | Rotate at least this often (1–3650 days). The current version is due this many days after its `NotBefore` (else `CreatedAt`). |
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
| `RotationIntervalDays` | `int?` | Rotate at least this often (1–3650 days). The current version is due this many days after its `NotBefore` (else `CreatedAt`). |
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

### ConfigurationRevision

One change to a configuration key in one environment, appended by a `SaveChanges` interceptor in the same transaction as the change, so every write path is covered. Revisions are numbered per key and survive deleting and re-creating it.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `Scope` | `int` | `0` application, `1` global |
| `ApplicationId` | `Guid?` | Owning application; `null` for global |
| `EnvironmentKey` | `string` | Environment id (`N` format) for application keys, environment slug (`*` included) for global keys |
| `Key` | `string` | Configuration key |
| `Revision` | `int` | 1, 2, 3… per (scope, environment, key); unique |
| `Change` | `string` | `baseline`, `create`, `update`, `delete` or `rollback` |
| `Value` | `string?` | Value after the change; `null` for `delete` |
| `ValueType` | `string` | As on the entry |
| `Description` | `string?` | As on the entry |
| `RolledBackFrom` | `int?` | For `rollback`, the restored revision |
| `ChangedBy` | `string` | `admin`, or `system` for the `baseline` written by the migration |
| `ChangedAt` | `DateTimeOffset` | — |

### RotationReminder

Remembers which [rotation reminders](../operations/rotation-reminders.md) were delivered, so each one is sent once. Holds no secret data.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `Key` | `string` | Unique. Scope, secret id, environment, reason, due date (ticks) and threshold. |
| `SentAt` | `DateTimeOffset` | Stored as an integer. Rows older than `RotationReminders:KeepSentDays` are deleted. |

### SecretRead

Rollup of successful Consumer API secret reads for the [usage reports](../api-reference/management-api.md#reports): one row per reading application environment and secret. Holds no values.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | Reading application (cascade delete) |
| `EnvironmentId` | `Guid` | Reading environment (cascade delete) |
| `SecretId` | `Guid?` | Application secret read (cascade delete) |
| `GlobalSecretId` | `Guid?` | Global secret read (cascade delete). Exactly one of the two is set. |
| `LastReadAt` | `DateTimeOffset` | Stored as an integer, like `AuditEvents.OccurredAt` |
| `ReadCount` | `long` | Approximate; incremented per flush |

Unique per (`EnvironmentId`, `SecretId`) and (`EnvironmentId`, `GlobalSecretId`). The migration seeds it from successful `secret.read` audit events.

### FeatureFlag

A [feature flag](../feature-flags/) of one application, or a global flag (`ApplicationId` null). Variants, tags, rules and fallthrough are JSON text columns.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid?` | Owning application (cascade delete); `null` for a global flag |
| `Key` | `string` | Unique per application, and among global flags |
| `Description` | `string?` | — |
| `Type` | `string` | `boolean` or `multivariate` |
| `Variants` | JSON | `[{ "key", "value" }]`; `value` is raw JSON text |
| `DefaultVariant` | `string` | — |
| `Tags` | JSON | `["…"]` |
| `Salt` | `string` | 16 hex characters, random per flag, mixed into bucketing |
| `CreatedAt` / `UpdatedAt` | `DateTimeOffset` | — |

### FeatureFlagEnvironmentState

A flag's state in one environment: an application environment (`EnvironmentId`) for application flags, or an environment slug (`EnvironmentSlug`, `*` for all) for global flags; exactly one is set. No row means disabled, serving the default variant.

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `FeatureFlagId` | `Guid` | Cascade delete; unique with `EnvironmentId`, and with `EnvironmentSlug` |
| `EnvironmentId` | `Guid?` | Application flags (cascade delete) |
| `EnvironmentSlug` | `string?` | Global flags: an environment slug or `*` |
| `Enabled` | `bool` | — |
| `OffVariant` | `string?` | — |
| `Rules` | JSON | Ordered targeting rules |
| `Fallthrough` | JSON? | `{ "variant" }` or `{ "rollout", "bucketBy" }` |
| `Version` | `int` | Incremented on every change; concurrency token |
| `UpdatedAt` | `DateTimeOffset` | — |

### AccessToken

A Consumer API credential for one application, narrower than its API key (cascade-deleted with the application).

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | FK → `Application` |
| `Name` | `string` | Unique per application (up to 100 chars) |
| `TokenHash` | `string` | PBKDF2 hash of the plain token (`grm_tok_` + 40 random characters) |
| `Prefix` | `string` | The 8 characters after `grm_tok_`; indexed, used to find the token before verifying the hash |
| `EnvironmentSlugs` | JSON? | Environment slugs the token may read; `null` for all |
| `Scopes` | `int` | Flags: `1` secrets, `2` configurations, `4` feature flags |
| `ExpiresAt` | `DateTimeOffset?` | `null` never expires |
| `LastUsedAt` | `DateTimeOffset?` | Written at most once a minute per token |
| `RevokedAt` | `DateTimeOffset?` | Set when revoked; the row is kept |
| `CreatedAt` | `DateTimeOffset` | Stored as an integer |
| `TrustPolicyId` | `Guid?` | Set for tokens issued by a GitHub OIDC exchange (not listed with the others; deleted a day after they expire) |

### TrustPolicy

Lets GitHub Actions runs exchange their OIDC token for a short-lived `AccessToken` (cascade-deleted with the application).

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid` | FK → `Application` |
| `Name` | `string` | Unique per application |
| `Repository` | `string` | `owner/repo`, compared case-insensitively |
| `RefPattern` | `string?` | e.g. `refs/heads/main`, `refs/tags/v*`; `null` for any ref |
| `GitHubEnvironment` | `string?` | Exact GitHub environment; `null` for any |
| `EnvironmentSlugs` | JSON? | Environments the issued token may read; `null` for all |
| `Scopes` | `int` | Same flags as `AccessToken.Scopes` |
| `TokenLifetimeMinutes` | `int` | 1–60 (default 15) |
| `LastUsedAt` | `DateTimeOffset?` | Last successful exchange |
| `CreatedAt` | `DateTimeOffset` | Stored as an integer |

### FeatureFlagAuditEntry

One change to a flag, kept after the flag is deleted (cascade-deleted only with the application).

| Column | Type | Notes |
| :----- | :--- | :---- |
| `Id` | `Guid` | Primary key |
| `ApplicationId` | `Guid?` | `null` for a global flag |
| `FlagKey` | `string` | — |
| `Environment` | `string?` | Environment slug for `environment` and `toggle` changes |
| `Change` | `string` | `create`, `update`, `environment`, `toggle` or `delete` |
| `Actor` | `string` | `admin` |
| `ChangedAt` | `DateTimeOffset` | Stored as an integer |
| `Before` / `After` | JSON? | Flag or environment state before and after |

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
