---
title: Consumer API
parent: API Reference
nav_order: 3
---

# 🔌 Consumer API
{: .no_toc }

All consumer endpoints require `X-Api-Key: <key>`. The key is scoped to a single application — it can only access that application's own secrets and configurations.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Secrets

### Get secret

```
GET /api/consumer/secrets/{name}?environment={envSlug}
```

Returns the **latest active version** of the named secret in the given environment.

A version is active when:
- `IsEnabled = true`
- `NotBefore IS NULL OR NotBefore <= now`
- `ExpiresAt IS NULL OR ExpiresAt >= now`

**Headers:**

```
X-Api-Key: grm_your_api_key_here
```

**Query parameters:**

| Parameter | Required | Description |
| :-------- | :------: | :---------- |
| `environment` | ✅ | Environment slug (e.g. `local`, `production`) |

**Response `200`:**

```json
{
  "name": "database-password",
  "value": "super-secret-db-pass",
  "properties": {
    "enabled":   true,
    "expiresOn": null,
    "notBefore": null,
    "version":   2,
    "createdOn": "2025-01-01T00:00:00Z",
    "updatedOn": null
  }
}
```

The response shape matches **Azure Key Vault** secret responses to allow transparent migration.

**Response `404`:** Returned when:

- The secret name does not exist
- The environment does not exist
- No active version exists for this secret/environment combination (disabled, expired, or not yet active)

{: .note }
A `404` on a version-level issue (disabled/expired) is intentional — the consumer should treat a missing active version as "secret unavailable" rather than exposing version state.

---

## Configurations

### Get all configurations

```
GET /api/consumer/configurations?environment={envSlug}
```

Returns all configuration entries for the current application in the given environment.

**Response `200`:**

```json
{
  "items": [
    { "key": "Feature:DarkMode",       "value": "true",  "label": "local" },
    { "key": "Feature:NewOnboarding",  "value": "false", "label": "local" },
    { "key": "MaxRetries",             "value": "3",     "label": "local" }
  ]
}
```

The `label` field contains the environment slug. This matches the **Azure App Configuration** response shape.

**Response `404`:** Environment not found.

---

### Get configuration by key

```
GET /api/consumer/configurations/{key}?environment={envSlug}
```

Returns a single configuration entry.

**Response `200`:**

```json
{
  "key":   "Feature:DarkMode",
  "value": "true",
  "label": "local"
}
```

**Response `404`:** Key or environment not found.

---

## Using colon-delimited keys

Configuration keys support colon (`:`) delimiters, which map directly to the .NET `IConfiguration` key hierarchy:

| Grimoire key | `IConfiguration` access |
| :----------- | :---------------------- |
| `Feature:DarkMode` | `config["Feature:DarkMode"]` |
| `Database:Host` | `config["Database:Host"]` or `config.GetSection("Database")["Host"]` |
| `MaxRetries` | `config["MaxRetries"]` |

This allows using `IOptions<T>` and `GetSection()` just as you would with `appsettings.json`.

---

## Error scenarios

| Scenario | Status |
| :------- | :----- |
| Missing `X-Api-Key` header | `401` |
| Invalid API key | `401` |
| Secret does not exist | `404` |
| Environment does not exist | `404` |
| Secret exists but no active version | `404` |
| Secret version is disabled | `404` |
| Secret version is expired | `404` |
| Secret version is not yet active (`NotBefore`) | `404` |

Grimoire deliberately returns `404` (not `410 Gone` or a status-specific code) for expired/disabled versions to avoid leaking information about the secret's lifecycle state.
