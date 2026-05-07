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
GET /api/management/applications
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
    "createdAt": "2025-01-01T00:00:00Z",
    "updatedAt": "2025-01-01T00:00:00Z"
  }
]
```

---

### Create application

```
POST /api/management/applications
```

Creates an application and automatically seeds a **`local`** environment.

**Request body:**

```json
{
  "name": "payments-api",
  "description": "Payment processing service"
}
```

| Field | Required | Constraints |
| :---- | :------: | :---------- |
| `name` | ✅ | 1–200 characters |
| `description` | ➖ | Optional |

**Response `201`:**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "payments-api",
  "slug": "payments-api",
  "description": "Payment processing service",
  "plainApiKey": "grm_4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a",
  "createdAt": "2025-01-01T00:00:00Z"
}
```

{: .important }
Save `plainApiKey`. It is shown once and cannot be retrieved. Call [rotate-key](#rotate-api-key) to get a new one if lost.

**Response `409`:** An application with the same slug already exists.

---

### Get application

```
GET /api/management/applications/{slug}
```

**Response `200`:** Application object (same shape as list item, without `plainApiKey`).  
**Response `404`:** Application not found.

---

### Update application

```
PUT /api/management/applications/{slug}
```

**Request body:**

```json
{
  "name": "Payments API v2",
  "description": "Updated description"
}
```

**Response `200`:** Updated application object.

---

### Delete application

```
DELETE /api/management/applications/{slug}
```

Soft-deletes the application. The slug becomes available for reuse.  
All child environments, secrets, and configurations are hidden (but not purged from the database).

**Response `204`:** No content.

---

### Rotate API key

```
POST /api/management/applications/{slug}/rotate-key
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
GET /api/management/applications/{slug}/environments
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
POST /api/management/applications/{slug}/environments
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
DELETE /api/management/applications/{slug}/environments/{envSlug}
```

**Response `204`:** No content.

---

## Secrets

### List secrets

```
GET /api/management/applications/{slug}/secrets
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
POST /api/management/applications/{slug}/secrets
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
POST /api/management/applications/{slug}/secrets/{name}/values
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
GET /api/management/applications/{slug}/secrets/{name}
```

Returns the secret definition — not the values.

---

### List secret versions

```
GET /api/management/applications/{slug}/secrets/{name}/versions/{environmentSlug}
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
DELETE /api/management/applications/{slug}/secrets/{name}
```

Permanently deletes the secret and all its versions.

**Response `204`:** No content.

---

## Configurations

### List configurations

```
GET /api/management/applications/{slug}/configurations
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
    "updatedAt": "..."
  }
]
```

---

### Create configuration

```
POST /api/management/applications/{slug}/configurations
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
| `value` | ✅ | Non-empty string |
| `description` | ➖ | Optional |

**Response `201`:** Created entry.  
**Response `409`:** Key already exists for that environment.

---

### Update configuration

```
PUT /api/management/applications/{slug}/configurations/{environmentSlug}/{key}
```

**Request body:**

```json
{ "value": "false", "description": "Updated description" }
```

**Response `200`:** Updated entry.  
**Response `404`:** Entry not found.

---

### Delete configuration

```
DELETE /api/management/applications/{slug}/configurations/{environmentSlug}/{key}
```

**Response `204`:** No content.
