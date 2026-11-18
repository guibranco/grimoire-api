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
    "updatedAt": "2025-01-01T00:00:00Z"
  }
]
```

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

## Environments

### List environments

```
GET /management/applications/{slug}/environments
```

**Response `200`:**

```json
[
  { "id": "...", "slug": "local",   "name": "Local",      "createdAt": "..." },
  { "id": "...", "slug": "staging", "name": "Staging",    "createdAt": "..." },
  { "id": "...", "slug": "prod",    "name": "Production", "createdAt": "..." }
]
```

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
    "updatedAt": "2025-01-01T00:00:00Z"
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
{ "name": "database-password", "description": "Main DB password" }
```

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

{: .note }
`expiresAt` must be after `notBefore` if both are set (validated server-side).

**Response `200`:** Empty body.  
**Response `404`:** Environment or secret not found.

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

Returns all versions for the named secret in the given environment, ordered newest-first.

**Response `200`:**

```json
[
  {
    "id": "...",
    "version": 2,
    "isEnabled": true,
    "expiresAt": null,
    "notBefore": null,
    "createdAt": "2025-06-01T00:00:00Z"
  },
  {
    "id": "...",
    "version": 1,
    "isEnabled": false,
    "expiresAt": "2025-05-31T23:59:59Z",
    "notBefore": null,
    "createdAt": "2025-01-01T00:00:00Z"
  }
]
```

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

**Response `200`:** Array of `{ id, name, description, createdAt, updatedAt }`. No values.

---

### Create global secret

```
POST /management/global/secrets
```

**Request body:**

```json
{ "name": "amqp-uri", "description": "CloudAMQP connection string" }
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `name` | ✅ | 1–200 characters, unique |
| `description` | ➖ | Up to 1000 characters |

**Response `201`:** Created secret metadata.  
**Response `409`:** A global secret with that name already exists.

---

### Get global secret metadata

```
GET /management/global/secrets/{name}
```

**Response `200`:** `{ id, name, description, createdAt, updatedAt }`.  
**Response `404`:** Not found.

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

For an environment, the consumer gets the latest active version for that slug, or if there is none, the latest active version for `*`.

**Response `200`:** Empty body.  
**Response `400`:** Invalid item (e.g. bad `environmentSlug`, empty `value`, `expiresAt` not after `notBefore`).  
**Response `404`:** Global secret not found.

---

### List global secret versions

```
GET /management/global/secrets/{name}/versions/{environmentSlug}
```

Version metadata for one environment slug (`*` allowed), newest first. Same shape as [List secret versions](#list-secret-versions); never includes values.

---

### Delete global secret

```
DELETE /management/global/secrets/{name}
```

Deletes the secret and all its versions.

**Response `204`:** No content.
