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
curl https://grimoire.example.com/management/applications \
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
curl https://grimoire.example.com/consumer/secrets/my-secret?environment=production \
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
curl -X POST https://grimoire.example.com/management/applications/my-app/rotate-key \
  -H "Authorization: Bearer your-admin-key"
```

The response contains the new plain key. The old key is invalidated immediately.

### Access tokens

Instead of the application key, give each consumer its own [access token]({{ site.baseurl }}/api-reference/management-api/#access-tokens) (`grm_tok_…`, same `X-Api-Key` header), limited to some environments and kinds of data, optionally expiring and revocable on its own.

### GitHub Actions (OIDC)

Workflows need no stored Grimoire secret at all: they exchange the OIDC token GitHub gives every job for a short-lived access token. Each application's [trust policies]({{ site.baseurl }}/api-reference/management-api/#trust-policies-github-oidc) decide which repositories, refs and GitHub environments may, and what the token can read.

```yaml
permissions:
  id-token: write   # lets the job request an OIDC token

steps:
  - name: Get a Grimoire token
    run: |
      oidc=$(curl -sSf -G -H "Authorization: bearer $ACTIONS_ID_TOKEN_REQUEST_TOKEN" \
        "$ACTIONS_ID_TOKEN_REQUEST_URL" --data-urlencode "audience=$GRIMOIRE_OIDC_AUDIENCE" | jq -r .value)
      token=$(curl -sSf https://grimoire.example.com/consumer/oidc/github \
        -H "Content-Type: application/json" \
        -d "{\"application\":\"my-app\",\"token\":\"$oidc\"}" | jq -r .token)
      echo "::add-mask::$token"
      echo "GRIMOIRE_TOKEN=$token" >> "$GITHUB_ENV"
    env:
      GRIMOIRE_OIDC_AUDIENCE: grimoire   # must match Oidc:GitHub:Audience on your instance
  - run: curl -sSf -H "X-Api-Key: $GRIMOIRE_TOKEN" "https://grimoire.example.com/consumer/secrets/DB_PASSWORD?environment=staging"
```

`GRIMOIRE_OIDC_AUDIENCE` must match the instance's `Oidc:GitHub:Audience` (default `grimoire`) (see [configuration]({{ site.baseurl }}/getting-started/configuration/#github-oidc)).

---

## Unauthenticated endpoints

The following endpoints are always accessible without credentials:

| Endpoint | Purpose |
| :------- | :------ |
| `GET /health` | Health check (used by Docker, load balancers) |
| `POST /consumer/oidc/github` | Exchanges a GitHub Actions OIDC token for an access token; the OIDC token is the credential |

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
