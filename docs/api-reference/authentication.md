---
title: Authentication
parent: API Reference
nav_order: 1
---

# 🔑 Authentication
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Two authentication planes

| Plane | Header format | Credential source |
| :---- | :------------ | :---------------- |
| **Management** | `Authorization: Bearer <token>` | `Management:AdminApiKey` config value |
| **Consumer** | `X-Api-Key: <key>` | Plain API key returned on application creation |

The two planes are completely independent. A consumer API key cannot call management endpoints, and the admin bearer token cannot call consumer endpoints.

---

## Management authentication

Include the admin bearer token in every management request:

```bash
curl https://grimoire.example.com/api/management/applications \
  -H "Authorization: Bearer your-admin-key"
```

The token is compared directly against `Management:AdminApiKey`. There is no expiry — the token is valid until the config value is changed.

### Getting the token

Set `Management:AdminApiKey` in your configuration:

```bash
# Docker env var
Management__AdminApiKey=your-strong-random-token
```

Store it in a password manager or secrets manager. There is no UI to retrieve it.

---

## Consumer authentication

Consumer API keys are per-application. The plain key is shown **once** when the application is created or its key is rotated.

```bash
curl https://grimoire.example.com/api/consumer/secrets/my-secret?environment=production \
  -H "X-Api-Key: grm_a1b2c3d4e5f6a1b2c3d4e5f6a1b2c3d4e5f6a1b2"
```

### Key format

```
grm_{40 lowercase hex chars}
```

Example: `grm_4f3a2b1c0d9e8f7a6b5c4d3e2f1a0b9c8d7e6f5a`

### Key storage

The plain key is **never stored** by Grimoire. Only the PBKDF2 hash is persisted. If you lose the key, [rotate it]({{ site.baseurl }}/api-reference/management-api/#rotate-api-key) via the Management API.

### Rotating a key

```bash
curl -X POST https://grimoire.example.com/api/management/applications/my-app/rotate-key \
  -H "Authorization: Bearer your-admin-key"
```

The response contains the new plain key. The old key is invalidated immediately.

---

## Unauthenticated endpoints

The following endpoints are always accessible without credentials:

| Endpoint | Purpose |
| :------- | :------ |
| `GET /health` | Health check (used by Docker, load balancers) |

---

## Error responses

### Missing credentials

```json
{
  "type": "https://tools.ietf.org/html/rfc7235#section-3.1",
  "title": "Unauthorized",
  "status": 401
}
```

### Invalid credentials

Same `401` response — Grimoire does not distinguish between missing and wrong credentials to prevent enumeration.
