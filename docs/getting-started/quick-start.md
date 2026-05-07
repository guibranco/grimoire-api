---
title: Quick Start
parent: Getting Started
nav_order: 3
---

# ⚡ Quick Start
{: .no_toc }

Create an application, store a secret, and read it back — in under five minutes.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## 0. Start the API

```bash
docker compose up -d
```

Wait for the health check to pass:

```bash
curl http://localhost:8080/health
# → 200 OK
```

Throughout this guide we use the default demo credentials. **Change these before production use.**

```bash
ADMIN_KEY="change-me-in-production"
BASE_URL="http://localhost:8080"
```

---

## 1. Create an application

A Grimoire **application** maps to one of your services (e.g. `payments-api`, `frontend`).

```bash
curl -s -X POST "$BASE_URL/api/management/applications" \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "my-service", "description": "My first Grimoire app"}' \
  | jq .
```

**Response:**

```json
{
  "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "name": "my-service",
  "slug": "my-service",
  "description": "My first Grimoire app",
  "plainApiKey": "grm_a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2",
  "createdAt": "2025-01-01T00:00:00Z"
}
```

{: .important }
Save the `plainApiKey` — it is shown **once** and cannot be retrieved again. If you lose it, use the rotate-key endpoint to generate a new one.

```bash
SLUG="my-service"
API_KEY="grm_a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"
```

A **`local`** environment is automatically created alongside the application.

---

## 2. Store a secret

Create a secret (a named slot), then set its value for the `local` environment:

```bash
# Create the secret slot
curl -s -X POST "$BASE_URL/api/management/applications/$SLUG/secrets" \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"name": "database-password"}' | jq .

# Set the value for the "local" environment
curl -s -X POST "$BASE_URL/api/management/applications/$SLUG/secrets/database-password/values" \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '[{"environmentSlug": "local", "value": "super-secret-db-pass", "isEnabled": true}]'
```

The value is immediately encrypted with AES-256-GCM before being stored.

---

## 3. Read the secret (Consumer API)

Use the application's API key to read back the secret:

```bash
curl -s "$BASE_URL/api/consumer/secrets/database-password?environment=local" \
  -H "X-Api-Key: $API_KEY" | jq .
```

**Response:**

```json
{
  "name": "database-password",
  "value": "super-secret-db-pass",
  "properties": {
    "enabled": true,
    "expiresOn": null,
    "notBefore": null,
    "version": 1,
    "createdOn": "2025-01-01T00:00:00Z",
    "updatedOn": null
  }
}
```

---

## 4. Store and read a configuration flag

```bash
# Create a config entry
curl -s -X POST "$BASE_URL/api/management/applications/$SLUG/configurations" \
  -H "Authorization: Bearer $ADMIN_KEY" \
  -H "Content-Type: application/json" \
  -d '{"environmentSlug": "local", "key": "Feature:DarkMode", "value": "true"}' | jq .

# Read it back
curl -s "$BASE_URL/api/consumer/configurations/Feature:DarkMode?environment=local" \
  -H "X-Api-Key: $API_KEY" | jq .
```

**Response:**

```json
{
  "key": "Feature:DarkMode",
  "value": "true",
  "label": "local"
}
```

---

## 5. Use the .NET client library

Add the `Grimoire.Consumer` project reference and wire it in `Program.cs`:

```csharp
builder.Configuration.AddGrimoire(
    baseUrl:     "http://localhost:8080",
    apiKey:      "grm_a1b2c3d4e5f6...",
    environment: "local"
);

// The secret and config are now available as normal IConfiguration keys
var dbPassword    = builder.Configuration["database-password"];
var darkModeOn    = builder.Configuration["Feature:DarkMode"];
```

See the [Client Library]({{ site.baseurl }}/client-library/) section for full details.

---

## What's next?

- Add more [environments]({{ site.baseurl }}/api-reference/management-api/#environments) (`staging`, `production`)
- Set `ExpiresAt` and `NotBefore` on secret versions for time-windowed access
- [Rotate API keys]({{ site.baseurl }}/api-reference/management-api/#rotate-api-key) without downtime
- Explore the interactive [Swagger UI](http://localhost:8080/swagger)
