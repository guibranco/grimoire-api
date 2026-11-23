---
title: Consumer API
parent: API Reference
nav_order: 3
---

# 🔌 Consumer API
{: .no_toc }

All consumer endpoints require `X-Api-Key: <key>`. The key is scoped to a single application — it can only access that application's own secrets and configurations, plus any [global values](#global-secrets-and-configuration) the application inherits.

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
GET /consumer/secrets/{name}?environment={envSlug}
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

- The secret name does not exist (neither on the application nor, for applications that inherit globals, as a global secret)
- The environment does not exist
- No active version exists for this secret/environment combination (disabled, expired, or not yet active)

{: .note }
A `404` on a version-level issue (disabled/expired) is intentional — the consumer should treat a missing active version as "secret unavailable" rather than exposing version state.

---

## Configurations

### Get all configurations

```
GET /consumer/configurations?environment={envSlug}
```

Returns all configuration entries for the current application in the given environment, merged with any inherited global entries (application values win on the same key).

**Response `200`:**

```json
{
  "items": [
    { "key": "Feature:DarkMode", "value": "true",                       "label": "local", "valueType": "string" },
    { "key": "MaxRetries",       "value": "3",                          "label": "local", "valueType": "string" },
    { "key": "AllowedIps",       "value": "[\"10.0.0.1\",\"10.0.0.2\"]", "label": "local", "valueType": "array" }
  ]
}
```

The `label` field contains the environment slug. This matches the **Azure App Configuration** response shape.

`valueType` is `string`, `multiline`, `array` (a JSON array of strings) or `json` (a JSON object or array). `value` is always a string; for `array` and `json` it holds the JSON text, so parse it when `valueType` says so. Clients written before value types existed ignore the field and keep working.

**Response `404`:** Environment not found.

---

### Get configuration by key

```
GET /consumer/configurations/{key}?environment={envSlug}
```

Returns a single configuration entry, falling back to an inherited global entry when the application doesn't define the key.

**Response `200`:**

```json
{
  "key":       "Feature:DarkMode",
  "value":     "true",
  "label":     "local",
  "valueType": "string"
}
```

**Response `404`:** Key or environment not found.

---

## Feature flags

Flag definitions for local evaluation: the API never evaluates for you and never sees your users' attributes. Evaluate with the [evaluation spec](../feature-flags/evaluation-spec/).

### Get all feature flags

```
GET /consumer/feature-flags?environment=production
```

```json
{
  "environment": "production",
  "flags": [
    {
      "key": "checkout-redesign",
      "type": "multivariate",
      "variants": [ { "key": "control", "value": { "layout": "classic" } }, { "key": "treatment", "value": { "layout": "one-page" } } ],
      "defaultVariant": "control",
      "salt": "9f2c4e1a7b3d5f60",
      "enabled": true,
      "offVariant": "control",
      "rules": [ { "conditions": [ { "attribute": "userId", "operator": "in", "values": ["u-123"] } ], "serve": { "variant": "treatment" } } ],
      "fallthrough": { "rollout": [ { "variant": "control", "weight": 80 }, { "variant": "treatment", "weight": 20 } ], "bucketBy": "userId" },
      "version": 4
    }
  ]
}
```

A flag never configured in the environment comes back disabled with `version` 0.

**Caching:** responses carry a strong `ETag`. Send it back in `If-None-Match` and the answer is `304 Not Modified` with no body until a flag changes. Polling clients should always do this. A `304` still counts as consumer activity.

### Get feature flag by key

```
GET /consumer/feature-flags/{key}?environment=production
```

One definition, same shape and `ETag` behaviour. **`404`:** unknown flag or environment.

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

## Global secrets and configuration

Administrators can define [global configuration and secrets](management-api.md#global-configurations) once for all applications. Consumers don't need to change anything: the same endpoints, API key and response shapes return the merged result, so existing clients (including `Grimoire.Consumer`) keep working.

For applications with `inheritGlobals: true` (the default), values resolve in this order, highest first:

1. The application's own value for the environment
2. A global value for the environment's slug (e.g. `production`)
3. A global value for `*` (all environments)

Applications with `inheritGlobals: false` only see their own values.

**Secret fallback rule:** a global secret is returned **only when the application has no secret with that name**. If the application defines the secret but has no active version for the environment (disabled, expired, not yet valid, or never set), the response is `404`. An explicit application-level disable is never silently replaced by a global value.

The `label` of a configuration item is always the requested environment slug, whether the value came from the application or a global entry.

---

## Error scenarios

| Scenario | Status |
| :------- | :----- |
| Missing `X-Api-Key` header | `401` |
| Invalid API key | `401` |
| Secret does not exist | `404` |
| Environment does not exist | `404` |
| Secret exists but no active version | `404` |
| Application secret has no active version, but a global secret with the same name does | `404` (no fallback) |
| Secret version is disabled | `404` |
| Secret version is expired | `404` |
| Secret version is not yet active (`NotBefore`) | `404` |

Grimoire deliberately returns `404` (not `410 Gone` or a status-specific code) for expired/disabled versions to avoid leaking information about the secret's lifecycle state.
