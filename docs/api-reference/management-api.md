---
title: Management API
parent: API Reference
nav_order: 2
---

# 🔧 Management API
{: .no_toc }

All management endpoints require `Authorization: Bearer <AdminApiKey>`.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Applications

### List applications

```
GET /management/applications
```

Returns all non-deleted applications.

**Response `200`:**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "name": "payments-api",
    "slug": "payments-api",
    "description": "Payment processing service",
    "inheritGlobals": true,
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-01T00:00:00Z",
    "lastSeenAt": "2025-01-02T09:30:00Z"
  }
]
```

`lastSeenAt` is the latest successful Consumer API read across the application's environments, or `null` if no consumer has read it yet.

---

### Create application

```
POST /management/applications
```

Creates an application and automatically seeds a **`local`** environment.

**Request body:**

```json
{
  "name": "payments-api",
  "description": "Payment processing service",
  "inheritGlobals": true
}
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `name` | ✅ | 1–200 characters |
| `description` | ➖ | Optional |
| `inheritGlobals` | ➖ | Defaults to `true`. When `false`, [global values](#global-configurations) don't apply to this application |

**Response `201`:**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "payments-api",
  "slug": "payments-api",
  "description": "Payment processing service",
  "plainApiKey": "grm_4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a",
  "createdAt": "2025-01-01T00:00:00Z",
  "inheritGlobals": true
}
```

{: .important }
Save `plainApiKey`. It is shown once and cannot be retrieved. Call [rotate-key](#rotate-api-key) to get a new one if lost.

**Response `409`:** An application with the same slug already exists.

---

### Get application

```
GET /management/applications/{slug}
```

**Response `200`:** Application object (same shape as list item, without `plainApiKey`).  
**Response `404`:** Application not found.

---

### Update application

```
PUT /management/applications/{slug}
```

**Request body:**

```json
{
  "name": "Payments API v2",
  "description": "Updated description",
  "inheritGlobals": false
}
```

`inheritGlobals` is optional; when omitted the current setting is kept.

**Response `200`:** Updated application object.

---

### Delete application

```
DELETE /management/applications/{slug}
```

Soft-deletes the application. The slug becomes available for reuse.  
All child environments, secrets, and configurations are hidden (but not purged from the database).

**Response `204`:** No content.

---

### Rotate API key

```
POST /management/applications/{slug}/rotate-key
```

Generates a new API key and invalidates the old one immediately.

**Response `200`:**

```json
{
  "plainApiKey": "grm_9d8c7b6a5f4e3d2c1b0a9f8e7d6c5b4a3f2e1d0c"
}
```

---

## Access tokens

Consumer API credentials narrower than the application's API key: each token can be limited to some environments and to some kinds of data, can expire, and can be revoked without affecting the API key or other tokens. Consumers send a token in the same `X-Api-Key` header. The API key keeps working and still reads every environment.

Grimoire stores only a PBKDF2 hash of each token. The plain token is returned once, when it is created.

### List access tokens

```
GET /management/applications/{slug}/access-tokens
```

**Response `200`** (newest first; revoked and expired tokens are included):

```json
[
  {
    "id": "5f0c…",
    "name": "github-actions",
    "prefix": "Ab3dE9xQ",
    "environments": ["staging"],
    "scopes": ["secrets", "configurations"],
    "expiresAt": "2027-02-21T00:00:00+00:00",
    "lastUsedAt": "2026-11-23T14:51:02+00:00",
    "revokedAt": null,
    "createdAt": "2026-11-23T14:50:00+00:00",
    "status": "active"
  }
]
```

| Field | Notes |
| :---- | :---- |
| `prefix` | The 8 characters after `grm_tok_`; identifies the token without revealing it |
| `environments` | Environment slugs the token may read; `null` means every environment |
| `scopes` | `secrets`, `configurations` and/or `feature-flags` |
| `lastUsedAt` | Updated at most once a minute per token |
| `status` | `active`, `expired` or `revoked` |

### Create access token

```
POST /management/applications/{slug}/access-tokens
```

```json
{
  "name": "github-actions",
  "environments": ["staging"],
  "scopes": ["secrets", "configurations"],
  "expiresAt": "2027-02-21T00:00:00Z"
}
```

| Field | Required | Notes |
| :---- | :------- | :---- |
| `name` | Yes | Up to 100 characters; unique within the application |
| `environments` | No | Existing environment slugs; omit (or `null`) for every environment, including ones added later |
| `scopes` | Yes | At least one of `secrets`, `configurations`, `feature-flags` |
| `expiresAt` | No | Must be in the future; omit for a token that never expires |

**Response `201`:**

```json
{
  "token": { "id": "5f0c…", "name": "github-actions", "prefix": "Ab3dE9xQ", "status": "active", "…": "…" },
  "plainToken": "grm_tok_Ab3dE9xQ…"
}
```

`400` for an empty name or scope list, an unknown scope or environment, or an expiry in the past; `409` when the name is taken.

### Revoke access token

```
POST /management/applications/{slug}/access-tokens/{id}/revoke
```

Stops the token at once and returns it with `status: "revoked"`. Revoking a revoked token changes nothing. Revoked tokens stay listed so their history remains readable.

---

## Environments

### List environments

```
GET /management/applications/{slug}/environments
```

**Response `200`:**

```json
[
  { "id": "...", "slug": "local",   "name": "Local",      "createdAt": "...", "lastSeenAt": null },
  { "id": "...", "slug": "staging", "name": "Staging",    "createdAt": "...", "lastSeenAt": "..." },
  { "id": "...", "slug": "prod",    "name": "Production", "createdAt": "...", "lastSeenAt": "..." }
]
```

`lastSeenAt` is the last successful Consumer API read for the environment, or `null` if never seen. See [Consumer activity](#consumer-activity).

---

### Create environment

```
POST /management/applications/{slug}/environments
```

**Request body:**

```json
{ "name": "Production" }
```

The slug is auto-generated from the name (e.g. `"Production"` → `"production"`).

**Response `201`:** Created environment.  
**Response `409`:** Duplicate slug.

---

### Delete environment

```
DELETE /management/applications/{slug}/environments/{envSlug}
```

**Response `204`:** No content.

---

## Secrets

### List secrets

```
GET /management/applications/{slug}/secrets
```

Returns secret metadata — **never** the decrypted values.

**Response `200`:**

```json
[
  {
    "id": "...",
    "name": "database-password",
    "description": null,
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-01T00:00:00Z",
    "rotationIntervalDays": 90
  }
]
```

---

### Create secret

```
POST /management/applications/{slug}/secrets
```

Creates a named secret slot. Values are set separately via [Set values](#set-secret-values).

**Request body:**

```json
{ "name": "database-password", "description": "Main DB password", "rotationIntervalDays": 90 }
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `name` | ✅ | 1–200 characters, unique in the application |
| `description` | ➖ | Up to 1000 characters |
| `rotationIntervalDays` | ➖ | `1`–`3650`. Rotate at least this often: the [current version](#how-the-served-version-is-chosen) is due this many days after its `notBefore` (else its creation). Shows up in [Expiring secrets](#list-expiring-secrets) and [rotation reminders](../operations/rotation-reminders.md). |

**Response `201`:**

```json
{
  "id": "...",
  "name": "database-password",
  "description": "Main DB password",
  "createdAt": "...",
  "requiredEnvironments": [
    { "slug": "local",   "name": "Local",   "valueProvided": false },
    { "slug": "staging", "name": "Staging", "valueProvided": false }
  ]
}
```

`requiredEnvironments` lists all existing environments and whether a value has been set for each.

---

### Set secret values

```
POST /management/applications/{slug}/secrets/{name}/values
```

Creates new versions of the secret for one or more environments. Each call **appends** a new version — it does not overwrite existing versions.

**Request body (array):**

```json
[
  {
    "environmentSlug": "local",
    "value": "dev-password-123",
    "isEnabled": true,
    "expiresAt": null,
    "notBefore": null
  },
  {
    "environmentSlug": "staging",
    "value": "stg-password-456",
    "isEnabled": true,
    "expiresAt": "2025-12-31T23:59:59Z"
  }
]
```

| Field | Required | Description |
| :---- | :------: | :---------- |
| `environmentSlug` | ✅ | Target environment |
| `value` | ✅ | Plain-text secret value (encrypted before storage) |
| `isEnabled` | ➖ | Default `true` |
| `expiresAt` | ➖ | ISO 8601 timestamp; version expires after this |
| `notBefore` | ➖ | ISO 8601 timestamp; version inactive before this |
| `previousVersionGraceDays` | ➖ | `0`–`3650`. Every other enabled version in that environment with no expiry (or a later one) expires this many days from now, in the same request. Omit to leave them untouched. |

{: .note }
`expiresAt` must be after `notBefore` if both are set (validated server-side).

**Response `200`:** Empty body.  
**Response `400`:** Invalid item (empty value, `expiresAt` not after `notBefore`, grace days out of range).  
**Response `404`:** Environment or secret not found.

#### How the served version is chosen

Consumers get the **highest version that is enabled and inside its `notBefore`/`expiresAt` window**. A new version with a future `notBefore` therefore takes over on its own at that time, and the previous version keeps being served until then. Use `previousVersionGraceDays` when rotating so old values stop working after an overlap, instead of staying valid forever.

---

### Update secret

```
PATCH /management/applications/{slug}/secrets/{name}
```

Changes the description and rotation schedule. Omitted fields keep their value; values are changed through [Set values](#set-secret-values).

```json
{ "rotationIntervalDays": 30 }
```

| Field | Description |
| :---- | :---------- |
| `description` | New description (up to 1000 characters). |
| `rotationIntervalDays` | New rotation interval (`1`–`3650`). |
| `clearDescription` | `true` removes the description. Can't be combined with `description`. |
| `clearRotationInterval` | `true` removes the rotation schedule. Can't be combined with `rotationIntervalDays`. |

**Response `200`:** The updated secret metadata.  
**Response `400`:** Out-of-range interval, or a field both set and cleared.  
**Response `404`:** Application or secret not found.

---

### Get secret metadata

```
GET /management/applications/{slug}/secrets/{name}
```

Returns the secret definition — not the values.

---

### List secret versions

```
GET /management/applications/{slug}/secrets/{name}/versions/{environmentSlug}
```

Returns all versions for the named secret in the given environment, ordered newest-first, with which one is served. Never includes values.

**Response `200`:**

```json
[
  {
    "id": "...",
    "version": 2,
    "isEnabled": true,
    "expiresAt": null,
    "notBefore": null,
    "createdAt": "2025-06-01T00:00:00Z",
    "isCurrent": true,
    "status": "current"
  },
  {
    "id": "...",
    "version": 1,
    "isEnabled": true,
    "expiresAt": "2025-06-08T00:00:00Z",
    "notBefore": null,
    "createdAt": "2025-01-01T00:00:00Z",
    "isCurrent": false,
    "status": "valid"
  }
]
```

| `status` | Meaning |
| :------- | :------ |
| `current` | Served to consumers now (`isCurrent: true`). |
| `valid` | Enabled and in its window, but a newer valid version is served. |
| `scheduled` | Enabled, `notBefore` still in the future. |
| `expired` | `expiresAt` has passed. |
| `disabled` | `isEnabled` is `false`. |

---

### Update secret version

```
PATCH /management/applications/{slug}/secrets/{name}/versions/{environmentSlug}/{version}?force=false
```

Enables or disables a version, or changes its start/expiry. **The value can't be changed**: add a new version instead. Omitted fields keep their current value.

```json
{ "isEnabled": false }
```

```json
{ "expiresAt": "2026-12-01T00:00:00Z", "clearNotBefore": true }
```

| Field | Description |
| :---- | :---------- |
| `isEnabled` | Enable or disable the version. |
| `notBefore`, `expiresAt` | New start / expiry (ISO 8601). |
| `clearNotBefore`, `clearExpiresAt` | `true` removes the start / expiry. Can't be combined with a new value for the same field. |

**Response `200`:** The updated version (same shape as an item of [List secret versions](#list-secret-versions)).  
**Response `400`:** `expiresAt` not after `notBefore` (after merging with the stored values), or a field both set and cleared.  
**Response `404`:** Application, secret, environment or version not found.  
**Response `409`:** The change would leave the secret with no valid version in this environment (for example disabling or expiring the only valid one). Add a new value first, or repeat with `?force=true`.

---

### Roll back a secret

```
POST /management/applications/{slug}/secrets/{name}/versions/{environmentSlug}/{version}/rollback
```

Copies the stored value of `version` into a new version, which becomes the current one. The value is never decrypted or returned. The new version is enabled, with no start or expiry date.

Optional body:

```json
{ "previousVersionGraceDays": 7 }
```

`previousVersionGraceDays` (`0`–`3650`) expires the other versions after a grace period, as when [setting a value](#set-secret-values).

**Response `200`:** The new version's metadata (same shape as [List secret versions](#list-secret-versions)).  
**Response `404`:** Application, secret, environment or version not found.

---

### Delete secret

```
DELETE /management/applications/{slug}/secrets/{name}
```

Permanently deletes the secret and all its versions.

**Response `204`:** No content.

---

## Configurations

### List configurations

```
GET /management/applications/{slug}/configurations
```

Returns all configuration entries for the application, across all environments.

**Response `200`:**

```json
[
  {
    "id": "...",
    "environmentSlug": "local",
    "key": "Feature:DarkMode",
    "value": "true",
    "description": null,
    "createdAt": "...",
    "updatedAt": "...",
    "valueType": "string"
  }
]
```

---

### Create configuration

```
POST /management/applications/{slug}/configurations
```

**Request body:**

```json
{
  "environmentSlug": "local",
  "key": "Feature:DarkMode",
  "value": "true",
  "description": "Toggle dark mode"
}
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `environmentSlug` | ✅ | Must exist |
| `key` | ✅ | Non-empty string |
| `value` | ✅ | Non-empty string, valid for `valueType` (see [Value types](#value-types)) |
| `description` | ➖ | Optional |
| `valueType` | ➖ | `string` (default), `multiline`, `array` or `json` |

**Response `201`:** Created entry.  
**Response `400`:** Unknown `valueType`, or a value that isn't valid for it.  
**Response `409`:** Key already exists for that environment.

#### Value types

| `valueType` | Accepts | Stored as |
| :---------- | :------ | :-------- |
| `string` | Any text | As given |
| `multiline` | Any text | Line endings normalised to `\n` |
| `array` | A JSON array of strings, **or** one item per line (blank lines and surrounding spaces are ignored) | Minified JSON array, e.g. `["10.0.0.1","10.0.0.2"]` |
| `json` | Any JSON object or array | Minified JSON |

```json
{
  "environmentSlug": "production",
  "key": "AllowedIps",
  "value": "10.0.0.1\n10.0.0.2\n192.168.1.0/24",
  "valueType": "array"
}
```

The value is always returned as a string; for `array` and `json` it is the JSON text. Clients use `valueType` to decide how to parse it (`Grimoire.Consumer` flattens them into configuration sections — see [Configuration Provider](../client-library/configuration-provider.md#arrays-and-json-values)).

---

### Update configuration

```
PUT /management/applications/{slug}/configurations/{environmentSlug}/{key}
```

**Request body:**

```json
{ "value": "false", "description": "Updated description" }
```

`valueType` is optional: leave it out to keep the entry's current type (the new value is checked against it), or send it to change the type.

**Response `200`:** Updated entry.  
**Response `400`:** Unknown `valueType`, or a value that isn't valid for the type.  
**Response `404`:** Entry not found.

---

### Delete configuration

```
DELETE /management/applications/{slug}/configurations/{environmentSlug}/{key}
```

**Response `204`:** No content.

---

### Effective configuration

```
GET /management/applications/{slug}/configurations/effective?environment={envSlug}
```

Returns the configuration the application actually receives for an environment, with [global values](#global-configurations) merged in, and where each value comes from. This is the same merge the Consumer API uses.

**Response `200`:**

```json
[
  {
    "key": "LogStream:Url",
    "value": "https://logs.example.com",
    "description": "Shared log endpoint",
    "source": "global",
    "globalEnvironmentSlug": "*",
    "overridesGlobal": false,
    "valueType": "string"
  },
  {
    "key": "MaxRetries",
    "value": "5",
    "description": null,
    "source": "application",
    "globalEnvironmentSlug": "production",
    "overridesGlobal": true,
    "valueType": "string"
  }
]
```

| Field | Description |
| :---- | :---------- |
| `source` | `application` or `global` |
| `globalEnvironmentSlug` | For a global value, the slug of the global entry that supplied it (`*` or an environment slug). For an application value that overrides a global, the slug of the global it shadows. Otherwise `null` |
| `overridesGlobal` | `true` when an application value hides a global value with the same key |

**Response `404`:** Application or environment not found.

---

### Configuration history

```
GET /management/applications/{slug}/configurations/{environmentSlug}/{key}/history
```

Every change to a key in an environment, newest first. Revisions are numbered per key, so history survives deleting and re-creating the key. Changes that don't alter the value, value type or description add no revision.

**Response `200`:**

```json
[
  { "revision": 3, "change": "rollback", "value": "safe", "valueType": "string", "description": null, "rolledBackFrom": 1, "changedBy": "admin", "changedAt": "2026-11-23T10:00:00Z" },
  { "revision": 2, "change": "update",   "value": "fast", "valueType": "string", "description": null, "rolledBackFrom": null, "changedBy": "admin", "changedAt": "2026-11-22T09:00:00Z" },
  { "revision": 1, "change": "create",   "value": "safe", "valueType": "string", "description": null, "rolledBackFrom": null, "changedBy": "admin", "changedAt": "2026-11-20T08:00:00Z" }
]
```

| `change` | Meaning |
| :------- | :------ |
| `baseline` | The value when history started (entries that existed before the upgrade). |
| `create` / `update` | The key was created or changed. |
| `delete` | The key was deleted (`value` is `null`). |
| `rollback` | An earlier revision was restored (`rolledBackFrom`). |

**Response `404`:** Application or environment not found, or the key has no history.

---

### Roll back a configuration

```
POST /management/applications/{slug}/configurations/{environmentSlug}/{key}/rollback/{revision}
```

Restores the value, value type and description the key had at `revision`, as a new `rollback` revision. If the key was deleted since, it is re-created.

**Response `200`:** The configuration entry.  
**Response `400`:** That revision deleted the key, so it has no value to restore.  
**Response `404`:** Application, environment or revision not found.

---

### Compare environments

```
GET /management/applications/{slug}/configurations/diff?from=staging&to=production&includeUnchanged=false
```

Compares the [effective configuration](#effective-configuration) (globals merged in) of two environments, key by key.

**Response `200`:**

```json
{
  "from": "staging",
  "to": "production",
  "items": [
    { "key": "Cdn",   "status": "added",   "from": null, "to": { "value": "on", "valueType": "string", "source": "application", "globalEnvironmentSlug": null } },
    { "key": "Debug", "status": "removed", "from": { "value": "true", "valueType": "string", "source": "application", "globalEnvironmentSlug": null }, "to": null },
    { "key": "Url",   "status": "changed", "from": { "value": "stg", "valueType": "string", "source": "application", "globalEnvironmentSlug": null }, "to": { "value": "prd", "valueType": "string", "source": "application", "globalEnvironmentSlug": null } }
  ]
}
```

`added` keys are only in `to`, `removed` keys only in `from`, and `changed` keys differ in value or value type (a different source alone is not a change). `includeUnchanged=true` also lists `unchanged` keys. Keys are sorted and compared case-sensitively.

**Response `404`:** Application or one of the environments not found.

---

## Secrets (effective)

### Effective secrets

```
GET /management/applications/{slug}/secrets/effective
```

Returns the secrets the application can read, including inherited [global secrets](#global-secrets). Metadata only, never values.

**Response `200`:**

```json
[
  { "name": "amqp-uri",          "description": "CloudAMQP", "source": "global",      "overridesGlobal": false },
  { "name": "database-password", "description": null,        "source": "application", "overridesGlobal": false }
]
```

{: .note }
Because this route is matched literally, an application secret named `effective` can't be read with `GET /management/applications/{slug}/secrets/effective`.

---

## Global configurations

Global configuration entries are defined once and inherited by every application with `inheritGlobals: true`. They target environments **by slug**: `production` applies to every application's `production` environment, and `*` applies to all environments. See [Data Model — Global values and precedence](../architecture/data-model.md#global-values-and-precedence) for the resolution rules.

### List global configurations

```
GET /management/global/configurations?environment={envSlug}
```

`environment` is optional and filters on the exact slug (use `*` to list only all-environment entries).

**Response `200`:**

```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "environmentSlug": "*",
    "key": "LogStream:Url",
    "value": "https://logs.example.com",
    "description": "Shared log endpoint",
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-01T00:00:00Z"
  }
]
```

---

### Create global configuration

```
POST /management/global/configurations
```

**Request body:**

```json
{
  "environmentSlug": "*",
  "key": "LogStream:Url",
  "value": "https://logs.example.com",
  "description": "Shared log endpoint"
}
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `environmentSlug` | ➖ | `*` (default when omitted) or a lowercase slug such as `production`. The environment doesn't have to exist in any application yet |
| `key` | ✅ | 1–500 characters |
| `value` | ✅ | String, valid for `valueType` |
| `description` | ➖ | Up to 1000 characters |
| `valueType` | ➖ | `string` (default), `multiline`, `array` or `json` — see [Value types](#value-types) |

**Response `201`:** Created entry.  
**Response `400`:** Invalid `environmentSlug` or `valueType`, or a value that isn't valid for the type.  
**Response `409`:** Key already exists for that environment slug.

---

### Update global configuration

```
PUT /management/global/configurations/{environmentSlug}/{key}
```

Use `*` as `{environmentSlug}` for all-environment entries.

**Request body:**

```json
{ "value": "https://logs-2.example.com", "description": "Updated description" }
```

As with application entries, `valueType` is optional and defaults to the entry's current type.

An application value replaces a global value of the same key **as a whole**: arrays and JSON objects are never merged.

**Response `200`:** Updated entry.  
**Response `404`:** Entry not found.

---

### Delete global configuration

```
DELETE /management/global/configurations/{environmentSlug}/{key}
```

**Response `204`:** No content.  
**Response `404`:** Entry not found.

---

### Global configuration history and rollback

```
GET  /management/global/configurations/{environmentSlug}/{key}/history
POST /management/global/configurations/{environmentSlug}/{key}/rollback/{revision}
```

Same as [Configuration history](#configuration-history) and [Roll back a configuration](#roll-back-a-configuration), for a global key and environment slug (`*` included).

---

### List environments

```
GET /management/global/environments
```

Distinct environment slugs across all non-deleted applications, to populate an environment picker for global values. When applications give the same slug different display names, the most common name is returned.

**Response `200`:**

```json
[
  { "slug": "local",      "name": "Local",      "applicationCount": 12 },
  { "slug": "production", "name": "Production", "applicationCount": 9 }
]
```

---

## Global secrets

Global secrets mirror application secrets. Values are encrypted at rest with the same AES-256-GCM key, and no management endpoint ever returns them. A consumer receives a global secret **only when its application has no secret with that name** (see the [Consumer API](consumer-api.md#global-secrets-and-configuration)).

### List global secrets

```
GET /management/global/secrets
```

**Response `200`:** Array of `{ id, name, description, createdAt, updatedAt, rotationIntervalDays }`. No values.

---

### Create global secret

```
POST /management/global/secrets
```

**Request body:**

```json
{ "name": "amqp-uri", "description": "CloudAMQP connection string", "rotationIntervalDays": 180 }
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `name` | ✅ | 1–200 characters, unique |
| `description` | ➖ | Up to 1000 characters |
| `rotationIntervalDays` | ➖ | `1`–`3650`, as for [application secrets](#create-secret). Evaluated per environment slug. |

**Response `201`:** Created secret metadata.  
**Response `409`:** A global secret with that name already exists.

---

### Get global secret metadata

```
GET /management/global/secrets/{name}
```

**Response `200`:** `{ id, name, description, createdAt, updatedAt, rotationIntervalDays }`.  
**Response `404`:** Not found.

---

### Update global secret

```
PATCH /management/global/secrets/{name}
```

Same body, rules and responses as [Update secret](#update-secret).

---

### Set global secret values

```
POST /management/global/secrets/{name}/values
```

Each item creates a new version for its environment slug.

**Request body:**

```json
[
  { "environmentSlug": "*",          "value": "amqps://shared", "isEnabled": true },
  { "environmentSlug": "production", "value": "amqps://prod",   "isEnabled": true, "expiresAt": "2026-12-31T00:00:00Z" }
]
```

| Field | Required | Description |
| :---- | :------: | :---------- |
| `environmentSlug` | ✅ | `*` or a lowercase slug |
| `value` | ✅ | Plaintext; encrypted before storage |
| `isEnabled` | ➖ | Defaults to `true` |
| `expiresAt` | ➖ | Version inactive after this |
| `notBefore` | ➖ | Version inactive before this |
| `previousVersionGraceDays` | ➖ | `0`–`3650`; expires the other enabled versions of that slug after this many days (see [Set secret values](#set-secret-values)) |

For an environment, the consumer gets the latest active version for that slug, or if there is none, the latest active version for `*`.

**Response `200`:** Empty body.  
**Response `400`:** Invalid item (e.g. bad `environmentSlug`, empty `value`, `expiresAt` not after `notBefore`).  
**Response `404`:** Global secret not found.

---

### List global secret versions

```
GET /management/global/secrets/{name}/versions/{environmentSlug}
```

Version metadata for one environment slug (`*` allowed), newest first. Same shape as [List secret versions](#list-secret-versions), including `isCurrent` and `status` within that slug; never includes values.

---

### Update global secret version

```
PATCH /management/global/secrets/{name}/versions/{environmentSlug}/{version}?force=false
```

Same body, rules and responses as [Update secret version](#update-secret-version). The `409` guardrail considers the versions of that environment slug (`*` or a specific slug).

---

### Roll back a global secret

```
POST /management/global/secrets/{name}/versions/{environmentSlug}/{version}/rollback
```

Same as [Roll back a secret](#roll-back-a-secret), for one environment slug.

---

### Delete global secret

```
DELETE /management/global/secrets/{name}
```

Deletes the secret and all its versions.

**Response `204`:** No content.

---

## Rotation

### List expiring secrets

```
GET /management/secrets/expiring?withinDays=30
```

Secrets due for rotation across all applications and globals, soonest first. Metadata only, never values. For each secret and environment, the due date is the earlier of the [current version](#how-the-served-version-is-chosen)'s `expiresAt` and its rotation date (`rotationIntervalDays` after its `notBefore`, else its creation). A newer version already scheduled (`notBefore`) to take over by then counts as the rotation being planned, so nothing is due.

| Query parameter | Default | Description |
| :-------------- | :------ | :---------- |
| `withinDays` | `30` | `0`–`3650`. Look-ahead window. Overdue and expired secrets are always included. |

**Response `200`:**

```json
{
  "generatedAt": "2026-11-23T09:00:00Z",
  "withinDays": 30,
  "items": [
    {
      "application": "billing",
      "applicationName": "Billing",
      "secret": "DB_PASSWORD",
      "environment": "production",
      "isGlobal": false,
      "version": 3,
      "reason": "rotation",
      "dueAt": "2026-11-30T09:00:00Z",
      "daysRemaining": 7,
      "rotationIntervalDays": 90
    }
  ]
}
```

| `reason` | Meaning |
| :------- | :------ |
| `expiry` | The current version's `expiresAt`. |
| `rotation` | The current version is older than `rotationIntervalDays` allows. |
| `expired` | No version is valid any more because the latest enabled one expired. |

`application` and `applicationName` are `null` for global secrets, whose `environment` may be `*`. `daysRemaining` counts UTC calendar days and is negative when overdue. Application secrets are only listed for environments they have a value in.

**Response `400`:** `withinDays` out of range.

---

### Send rotation reminders now

```
POST /management/secrets/expiring/notify
```

Runs the [rotation reminder](../operations/rotation-reminders.md) job immediately (it also runs every `RotationReminders:IntervalHours`). Each reminder is delivered once; already-delivered ones are skipped.

**Response `200`:**

```json
{ "due": 3, "sent": 2, "alreadySent": 1, "webhookConfigured": true, "delivered": true }
```

`webhookConfigured: false` means reminders are due but `RotationReminders:WebhookUrl` is not set, so nothing was sent.

**Response `502`:** The webhook call failed (same body, `delivered: false`). Nothing is marked as sent, so the next run retries.

---

## Feature flags

Flags of one application: definitions, per-environment state and targeting. Consumers evaluate them locally; see [Feature Flags](../feature-flags/) and the [evaluation spec](../feature-flags/evaluation-spec/). Every change is kept in the flag's [history](#flag-history) and in the [audit log](#audit-log).

### List feature flags

```
GET /management/applications/{slug}/feature-flags?tag=&environment=&enabled=
```

Flags by key, each with its state in every environment of the application.

| Query parameter | Description |
| :-------------- | :---------- |
| `tag` | Only flags with this tag. |
| `environment` + `enabled` | Only flags enabled (`true`) or disabled (`false`) in that environment. |

**Response `200`:** an array of flags (see [Get feature flag](#get-feature-flag)). **`404`:** unknown application or environment.

---

### Create feature flag

```
POST /management/applications/{slug}/feature-flags
```

```json
{
  "key": "checkout-redesign",
  "description": "New checkout page layout",
  "type": "multivariate",
  "variants": [
    { "key": "control", "value": { "layout": "classic" } },
    { "key": "treatment", "value": { "layout": "one-page" } }
  ],
  "defaultVariant": "control",
  "tags": ["checkout", "q4"]
}
```

| Field | Notes |
| :---- | :---- |
| `key` | Required. Letters, digits, `.`, `_`, `:` and `-`, starting with a letter or digit; ≤ 500 characters. |
| `type` | `boolean` (default) or `multivariate`. |
| `variants` | Multivariate only: 2–20, unique keys (letters, digits, `.`, `_`, `-`), any JSON value. Boolean flags always have `on` (`true`) and `off` (`false`). |
| `defaultVariant` | Default `off` for boolean flags, else the first variant. |
| `tags` | ≤ 20; trimmed, de-duplicated and sorted. |

The flag starts disabled in every environment, with a random `salt` for bucketing.

**Response `201`:** the flag. **`400`:** validation. **`404`:** unknown application. **`409`:** the key exists.

---

### Get feature flag

```
GET /management/applications/{slug}/feature-flags/{key}
```

**Response `200`:**

```json
{
  "key": "checkout-redesign",
  "description": "New checkout page layout",
  "type": "multivariate",
  "variants": [ { "key": "control", "value": { "layout": "classic" } }, { "key": "treatment", "value": { "layout": "one-page" } } ],
  "defaultVariant": "control",
  "tags": ["checkout", "q4"],
  "createdAt": "2026-11-23T09:00:00Z",
  "updatedAt": "2026-11-23T09:00:00Z",
  "environments": [
    { "environment": "production", "enabled": false, "offVariant": null, "rules": [], "fallthrough": null, "version": 0, "updatedAt": null },
    {
      "environment": "staging",
      "enabled": true,
      "offVariant": "control",
      "rules": [
        { "conditions": [ { "attribute": "userId", "operator": "in", "values": ["u-123", "u-456"] } ], "serve": { "variant": "treatment" } }
      ],
      "fallthrough": { "rollout": [ { "variant": "control", "weight": 80 }, { "variant": "treatment", "weight": 20 } ], "bucketBy": "userId" },
      "version": 4,
      "updatedAt": "2026-11-23T09:30:00Z"
    }
  ]
}
```

`version` 0 means the flag was never configured in that environment (disabled, serving the default variant).

---

### Update feature flag

```
PUT /management/applications/{slug}/feature-flags/{key}
```

```json
{ "description": "Checkout v2", "tags": ["growth"], "variants": [ … ], "defaultVariant": "control" }
```

Replaces the description and tags, and for multivariate flags the variants and default variant. `variants` omitted keeps them. The type can't change. A variant still used by an environment's off variant, rules or fallthrough can't be removed.

**Response `200`:** the flag. **`400`:** validation, a boolean flag's variants, an unknown default or a variant in use. **`404`:** unknown application or flag.

---

### Delete feature flag

```
DELETE /management/applications/{slug}/feature-flags/{key}
```

Deletes the flag and its state in every environment. Its history is kept.

**Response `204`**. **`404`:** unknown application or flag.

---

### Set environment targeting

```
PUT /management/applications/{slug}/feature-flags/{key}/environments/{environmentSlug}
```

Replaces the flag's state in one environment and increments its `version`.

```json
{
  "enabled": true,
  "offVariant": "control",
  "rules": [
    {
      "conditions": [
        { "attribute": "email", "operator": "endsWith", "values": ["@straccini.com"] },
        { "attribute": "country", "operator": "in", "values": ["IE", "BR", "PT"] }
      ],
      "serve": { "rollout": [ { "variant": "control", "weight": 50 }, { "variant": "treatment", "weight": 50 } ], "bucketBy": "userId" }
    }
  ],
  "fallthrough": { "variant": "control" }
}
```

| Rule | |
| :--- | :- |
| `serve` | Exactly one of `variant` or `rollout`. |
| `rollout` | Weights 0–100, at most two decimals, summing to 100; each variant once. |
| `rules` | ≤ 100, each with 1–20 conditions. |
| `operator` | `in`, `notIn`, `equals`, `contains`, `startsWith`, `endsWith`, `matches` (a valid regex), `semVerGte`, `semVerLt` (exactly one valid version). |
| `values` | 1–500. |
| Variants | `offVariant`, served variants and rollout variants must exist on the flag. |

**Response `200`:** the whole flag. **`400`:** validation. **`404`:** unknown application, flag or environment.

---

### Toggle

```
POST /management/applications/{slug}/feature-flags/{key}/environments/{environmentSlug}/toggle
```

Kill switch: `{ "enabled": false }` sets the state, and no body flips it. Targeting is kept. **Response `200`:** the whole flag.

---

### Evaluate (dry run)

```
POST /management/applications/{slug}/feature-flags/{key}/evaluate
```

```json
{ "environment": "production", "context": { "userId": "u-789", "email": "gui@straccini.com", "country": "IE" } }
```

**Response `200`:**

```json
{ "variant": "treatment", "value": { "layout": "one-page" }, "reason": "RULE_MATCH", "matchedRuleIndex": 1, "bucket": 3127 }
```

`reason` is `OFF`, `RULE_MATCH`, `FALLTHROUGH` or `MISSING_BUCKET_BY`. `bucket` (0–9999) is set when a rollout bucketed the context. The context is not stored. Numbers and booleans in it are compared as their JSON text.

---

### Flag history

```
GET /management/applications/{slug}/feature-flags/{key}/history
```

Every change, newest first, kept after the flag is deleted.

```json
[
  {
    "change": "toggle",
    "environment": "production",
    "actor": "admin",
    "changedAt": "2026-11-23T10:00:00Z",
    "before": { "enabled": true, "offVariant": "control", "rules": [ … ], "fallthrough": { … }, "version": 3 },
    "after": { "enabled": false, "offVariant": "control", "rules": [ … ], "fallthrough": { … }, "version": 4 }
  }
]
```

`change` is `create`, `update` (the flag itself), `environment` (targeting), `toggle` or `delete`. `before` is `null` for a create or a first configuration; `after` is `null` for a delete.

**Response `404`:** unknown application, or no history for the key.

---

## Global feature flags

```
/management/global/feature-flags
```

Global flags reach every application that [inherits globals](#create-application), unless the application has a flag with the same key: application flags always win. The endpoints match the [application ones](#feature-flags), without `{slug}`. A global flag and an application flag may share a key.

- **State per environment slug.** `PUT …/{key}/environments/{environmentSlug}` and `POST …/toggle` take `*` (every environment) or any environment slug, including one no application has yet.
- **Precedence**, the same as [global configuration](#global-configurations):
  1. The application's own flag.
  2. The global flag's state for the environment's slug.
  3. The global flag's `*` state.
  4. Disabled.
- **Listing:** `GET` lists each flag's state for `*`, every slug any application uses, and every slug the flag has state for. `?environment=` accepts `*` or a slug.
- **Dry run:** `POST …/{key}/evaluate` with `{ "environment": "production" }` uses the slug's state, else `*`.
- **History:** `GET …/{key}/history` keeps global history separate from application flags' history.
- **Audit codes:** `global-feature-flag.create`, `.update`, `.delete`, `.environment-update` and `.toggle`.

---

## Reports

Built from Consumer API reads. Every successful secret read is recorded per reading application environment and secret (when, roughly how often), buffered in memory and written every `ConsumerActivity:FlushIntervalSeconds`; reports also include reads not written yet. Upgrading seeds the rollup from successful `secret.read` [audit events](#audit-log), so history starts as far back as the audit log goes. Metadata only, never values.

### List unused secrets

```
GET /management/reports/unused-secrets?days=90
```

Application and global secrets with no successful read in the last `days` days, including secrets never read. Never-read secrets come first, then the longest unread.

| Query parameter | Default | Description |
| :-------------- | :------ | :---------- |
| `days` | `90` | `1`–`3650`. Secrets read within this window are left out. |

**Response `200`:**

```json
{
  "generatedAt": "2026-11-23T09:00:00Z",
  "days": 90,
  "since": "2026-08-25T09:00:00Z",
  "items": [
    {
      "application": "billing",
      "applicationName": "Billing",
      "secret": "LEGACY_FTP_PASSWORD",
      "isGlobal": false,
      "createdAt": "2025-03-01T10:00:00Z",
      "lastReadAt": "2026-05-02T08:15:00Z",
      "readCount": 1284,
      "reads": [
        { "application": "billing", "environment": "production", "lastReadAt": "2026-05-02T08:15:00Z", "readCount": 1200 },
        { "application": "billing", "environment": "staging", "lastReadAt": "2026-04-30T11:00:00Z", "readCount": 84 }
      ]
    }
  ]
}
```

`application` and `applicationName` are `null` for global secrets; their `reads` list every application that read them. `lastReadAt` is `null` (and `reads` empty) for a secret never read. `readCount` is approximate. Secrets of deleted applications are not listed.

**Response `400`:** `days` out of range.

---

### Dependency graph

```
GET /management/reports/dependencies
```

Applications, secrets and configuration keys as a graph, for the UI's dependency view or other tooling.

**Response `200`:**

```json
{
  "generatedAt": "2026-11-23T09:00:00Z",
  "nodes": [
    { "id": "app:billing", "type": "application", "label": "Billing", "application": null, "environments": [] },
    { "id": "secret:billing/DB_PASSWORD", "type": "secret", "label": "DB_PASSWORD", "application": "billing", "environments": [] },
    { "id": "global-secret:SMTP_PASSWORD", "type": "global-secret", "label": "SMTP_PASSWORD", "application": null, "environments": [] },
    { "id": "config:billing/Timeout", "type": "config-key", "label": "Timeout", "application": "billing", "environments": ["production", "staging"] },
    { "id": "global-config:Region", "type": "global-config-key", "label": "Region", "application": null, "environments": ["*"] }
  ],
  "edges": [
    { "source": "app:billing", "target": "secret:billing/DB_PASSWORD", "kind": "owns", "environments": [], "lastReadAt": null, "readCount": null },
    { "source": "app:billing", "target": "global-secret:SMTP_PASSWORD", "kind": "reads", "environments": ["production"], "lastReadAt": "2026-11-23T08:59:00Z", "readCount": 512 },
    { "source": "app:billing", "target": "global-config:Region", "kind": "inherits", "environments": [], "lastReadAt": null, "readCount": null }
  ]
}
```

| Node `type` | `id` |
| :---------- | :--- |
| `application` | `app:{slug}` |
| `secret` | `secret:{slug}/{name}` |
| `global-secret` | `global-secret:{name}` |
| `config-key` | `config:{slug}/{key}` (one node per key across environments) |
| `global-config-key` | `global-config:{key}` (one node per key across `*` and environment slugs) |

| Edge `kind` | From → to |
| :---------- | :-------- |
| `owns` | Application → its secret or configuration key. |
| `reads` | Application → secret or global secret it read through the Consumer API, with the reading environments, last read and approximate count. |
| `overrides` | Application secret or key → global of the same name, when the application inherits globals. |
| `inherits` | Application → global configuration key it receives (inherits globals and doesn't override the key). |

Deleted applications are left out. Nodes and edges are sorted by id.

---

## Dashboard

### Get dashboard summary

```
GET /management/dashboard?expiringWithinDays=14&staleAfterDays=7
```

One aggregate, read-only call for the UI home page. Returns **metadata only, never secret or configuration values**. Soft-deleted applications are excluded. The response is sent with `Cache-Control: private, no-store`; the server reuses a computed summary for `Dashboard:CacheSeconds` (15 s by default).

| Query parameter | Default | Description |
| :-------------- | :------ | :---------- |
| `expiringWithinDays` | `Dashboard:ExpiringWithinDays` (14) | `0`–`3650`. Window for `secret-expiring` and `secret-rotation-due`. |
| `staleAfterDays` | `Dashboard:StaleAfterDays` (7) | `1`–`3650`. Threshold for `consumer-stale`. |

**Response `200`:**

```json
{
  "generatedAt": "2026-11-19T19:40:00Z",
  "totals": { "applications": 6, "environments": 14, "secrets": 41, "configurations": 128 },
  "applications": [
    {
      "slug": "payslip-archiver",
      "name": "Payslip Archiver",
      "inheritGlobals": true,
      "environments": [
        { "slug": "production", "name": "Production", "secretCount": 5, "configurationCount": 12, "lastChangedAt": "2026-11-18T10:02:11Z", "lastSeenAt": null }
      ],
      "secretCount": 5,
      "configurationCount": 12,
      "lastChangedAt": "2026-11-18T10:02:11Z",
      "lastSeenAt": null
    }
  ],
  "global": { "secretCount": 3, "configurationCount": 7, "lastChangedAt": "2026-11-15T21:00:00Z" },
  "featureFlags": null,
  "recentActivity": null,
  "attention": [
    { "kind": "secret-expiring", "severity": "warning", "application": "loop-requests", "environment": "production", "target": "DB_PASSWORD", "dueAt": "2026-11-28T00:00:00Z", "lastSeenAt": null }
  ]
}
```

- `secretCount` on an application counts its own secrets (globals are reported under `global`); on an environment it counts the application secrets with at least one version there.
- `lastChangedAt` is the latest `UpdatedAt` / secret-version `CreatedAt` across the application, its environments, secrets and configuration.
- `lastSeenAt` is the last successful consumer read (see [Consumer activity](#consumer-activity)); `null` when no consumer has read that environment yet.
- `featureFlags` is `null` until feature flags ship.
- `recentActivity` lists the latest 20 changes from the [audit log](#audit-log) (consumer reads and rejected requests left out), each with `action` as a readable description (e.g. "Added secret version") and `code` as the action code. It is `null` when `Audit:Enabled` is `false`.

**Attention items** are ordered by severity (`error`, `warning`, `info`). `application` is `null` for global secrets and configuration; `target` is a secret name or configuration key.

| Kind | Severity | Rule |
| :--- | :------- | :--- |
| `secret-expired` | error | The latest enabled version in an environment has expired and no other version is valid. |
| `secret-no-valid-version` | error | The secret has versions in an environment but none is enabled and inside its `notBefore`/`expiresAt` window, or it has no versions at all (`environment` is `null`). |
| `secret-expiring` | warning | The active version expires within `expiringWithinDays`. |
| `secret-rotation-due` | warning | The active version is due for rotation (its secret's `rotationIntervalDays`) within `expiringWithinDays`, before it expires. |
| `consumer-stale` | warning | Last consumer read is older than `staleAfterDays`. |
| `consumer-never-seen` | info | The environment has secrets or configuration but no consumer read was recorded. |
| `config-empty-value` | info | A configuration value is empty or whitespace. |

Environments a secret was never given a value in (such as the auto-created `local`) are not flagged. A global secret for an environment slug is only flagged when the `*` value cannot cover it either.

**Response `400`:** Threshold out of range.

---

### Consumer activity

Every successful (`2xx`) Consumer API read records, per application and environment, the time, the client's `User-Agent` and IP address, and an approximate request count. Failed and unauthorised calls are not recorded. No keys, values or request bodies are stored.

Reads are buffered in memory and written to the database every `ConsumerActivity:FlushIntervalSeconds` (60 s by default) and on shutdown, so a consumer read never waits on a database write. Management responses include reads that haven't been flushed yet. If the process stops abruptly, the last interval of activity can be lost; this affects `lastSeenAt` only.

Behind a reverse proxy, the recorded IP is the proxy's unless forwarded headers are configured.

---

## Search

### Search names and keys

```
GET /management/search?q=smtp&limit=20
```

Finds applications (name, slug, description), secrets (name, description) and configuration keys (key, description), both per application and global. Case-insensitive. Returns **where each one lives, never values**, and never matches on values. Soft-deleted applications are excluded. Sent with `Cache-Control: private, no-store`.

| Query parameter | Default | Description |
| :-------------- | :------ | :---------- |
| `q` | — | Required, at least 2 characters after trimming. `%` and `_` match literally. |
| `limit` | `20` | `1`–`50`. |

**Response `200`:**

```json
{
  "query": "smtp",
  "results": [
    { "type": "application", "name": "SMTP Relay", "application": "smtp-relay", "environments": [], "description": "Outbound mail", "valueType": null },
    { "type": "configuration", "name": "SMTP_HOST", "application": "payslip-archiver", "environments": ["local", "production"], "description": null, "valueType": "string" },
    { "type": "secret", "name": "SMTP_PASSWORD", "application": "payslip-archiver", "environments": ["production"], "description": null, "valueType": null },
    { "type": "global-configuration", "name": "SMTP_PORT", "application": null, "environments": ["*"], "description": null, "valueType": "string" },
    { "type": "global-secret", "name": "SMTP_RELAY_KEY", "application": null, "environments": ["*", "production"], "description": "relay", "valueType": null }
  ],
  "truncated": false
}
```

- `type`: `application`, `secret`, `configuration`, `global-secret` or `global-configuration`.
- A secret or key appears once per application (or once globally), with `environments` listing where it has a value. `*` means every environment.
- **Ranking:** exact match, then prefix, then substring on the name/key (or application slug), then description-only matches. Ties go to applications first, then secrets, configuration, global secrets and global configuration, then by name.
- `truncated` is `true` when more results matched than `limit`.

**Response `400`:** `q` shorter than 2 characters, or `limit` out of range.

---

## Audit log

Grimoire records who read or changed what. Every management change (applications, environments, secrets, configuration, Global entries, backups), every Consumer API read, and every request rejected by an API key check produces one event. Events record names, keys and slugs only, **never secret or configuration values**.

Events are queued in memory and written in the background, so requests never wait on the audit write. Each event is also written to the server log as a structured entry (and so reaches LogStream when configured). Events older than `Audit:RetentionDays` (180 by default) are deleted daily.

### List audit events

```
GET /management/audit?application=payslip-archiver&action=secret.*&includeReads=false&page=1&pageSize=50
```

Newest first. All filters are optional. Sent with `Cache-Control: private, no-store`.

| Query parameter | Description |
| :-------------- | :---------- |
| `application` | Application slug. |
| `action` | Action code, or a prefix ending in `*` (`secret.*`). |
| `actor` | `admin`, `app:<slug>` (API key), `app:<slug>/token:<prefix>` (access token) or `anonymous`. |
| `environment` | Environment slug (also matches events that touched several environments). |
| `target` | Secret name or configuration key. |
| `outcome` | `success`, `notfound`, `forbidden`, `invalid`, `conflict` or `error`. |
| `from`, `to` | ISO 8601 bounds, inclusive. |
| `includeReads` | `false` leaves out consumer reads (`*.read`). Default `true`. |
| `page`, `pageSize` | 1-based page; `pageSize` 1–200, default 50. |

**Response `200`:**

```json
{
  "items": [
    {
      "id": "0193…",
      "occurredAt": "2026-11-22T23:10:04Z",
      "actor": "app:payslip-archiver",
      "action": "secret.read",
      "description": "Read secret",
      "application": "payslip-archiver",
      "environment": "production",
      "targetType": "secret",
      "target": "MAILBOX_PASSWORD",
      "version": null,
      "outcome": "success",
      "statusCode": 200,
      "ipAddress": "10.0.0.5",
      "userAgent": "Grimoire.Consumer/1.4.0"
    }
  ],
  "page": 1,
  "pageSize": 50,
  "totalCount": 1
}
```

| Action | Description |
| :----- | :---------- |
| `application.create` / `.update` / `.delete` / `.rotate-key` | Application changes. |
| `environment.create` / `.delete` | Environment changes (`target` is the name). |
| `secret.create` / `.update` / `.write` / `.delete` | Secret changes; `.update` changes the description or rotation interval, `.write` adds a version (`environment` lists the environments set). |
| `secret.version-update` | A version was enabled/disabled or its window changed (`version` is set). |
| `secret.rollback` | An earlier version was copied into a new version (`version` is the new one). |
| `configuration.create` / `.update` / `.delete` / `.rollback` | Configuration changes; every one also adds a [revision](#configuration-history). |
| `global-secret.*`, `global-configuration.*` | The same for Global entries (`application` is `null`), including `global-secret.version-update` and the `.rollback` actions. |
| `backup.create` | On-demand backup. |
| `feature-flag.create`, `.update`, `.delete`, `.environment-update`, `.toggle` | Feature flag changes (also in the [flag history](#flag-history)). |
| `feature-flag.read` | Consumer read of flag definitions (a read, like `secret.read`). |
| `global-feature-flag.create`, `.update`, `.delete`, `.environment-update`, `.toggle` | [Global feature flag](#global-feature-flags) changes. |
| `rotation.remind` | Rotation reminders sent on demand (`POST /management/secrets/expiring/notify`). Scheduled runs are logged, not audited. |
| `secret.read`, `configuration.read` | Consumer API reads, by `app:<slug>` or `app:<slug>/token:<prefix>`. Failed reads are recorded with their outcome. |
| `access-token.create`, `.revoke` | [Access token](#access-tokens) changes; `target` is the token name. |
| `access-token.denied` | A valid access token used for an environment or scope it doesn't cover (`403`); `target` is the method and path. |
| `auth.failed` | Request rejected by the admin or consumer API key check; `target` is the method and path. |

Invalid requests rejected by model validation before the action runs (`400`) are not recorded.

**Response `400`:** Invalid `outcome`, `page`, `pageSize`, or `from` after `to`.

---

## Backups

Encrypted snapshots of the database. Both endpoints answer `409` until backups are configured (`Backup:Enabled`, `Backup:Key` and at least one target); see [Backup and restore](../operations/backup-and-restore.md).

### List backups

```
GET /management/backups
```

Backups found in every enabled target, newest first. A backup stored in both targets appears once, with both target names.

**Response `200`:**

```json
[
  {
    "name": "grimoire-20261119T030000001Z.gbak",
    "sizeBytes": 18432,
    "createdAt": "2026-11-19T03:00:00.001+00:00",
    "targets": ["local", "s3"]
  }
]
```

**Response `409`:** Backups are not configured.

---

### Create backup

```
POST /management/backups
```

Takes a backup now, writes it to every enabled target, applies retention and pings `Backup:HealthcheckUrl`. Concurrent requests run one at a time.

**Response `201`:** The new backup (same shape as a list item).  
**Response `409`:** Backups are not configured.  
**Response `500`:** At least one target failed. Details are in the server log.

