---
title: Security
parent: Architecture
nav_order: 3
---

# 🔒 Security Model
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Authentication model

Grimoire has two distinct authentication planes:

| Plane | Header | Who uses it | What it grants |
| :---- | :----- | :---------- | :------------- |
| **Management** | `Authorization: Bearer <key>` | Admins / CI pipelines | Full CRUD on all resources |
| **Consumer** | `X-Api-Key: <key>` | Application services | Read-only access to own secrets and configs |

The separation ensures that compromising a consumer API key does not expose management operations, and vice versa.

---

## Encryption at rest

All secret values are encrypted before being written to the database. Configuration entries are **not** encrypted.

### Key derivation

```text
Encryption:MasterKey (from appsettings / env var)
         │
         ▼
  HKDF-SHA256 (info = "grimoire-aes-key", salt = none)
         │
         ▼
  256-bit AES key  (cached in-process as singleton)
```

`HKDF.DeriveKey` (from `System.Security.Cryptography`) is used for key derivation. This means you can rotate the master key without losing old secrets *if* you re-encrypt the database first.

### Encryption

```text
plaintext (UTF-8 bytes)
      │
      ▼
AesGcm.Encrypt(
  nonce  = 12 random bytes (RFC 5116),
  key    = derived 256-bit key,
  tagSize = 16 bytes
)
      │
      ▼
stored = Base64( nonce[12] ∥ tag[16] ∥ ciphertext )
```

### Decryption

```text
stored Base64
      │
      ▼
split: nonce[0..12], tag[12..28], ciphertext[28..]
      │
      ▼
AesGcm.Decrypt(nonce, ciphertext, tag)
      │
      ▼
plaintext
```

The 16-byte authentication tag means any tampering with the ciphertext or nonce produces an `AuthenticationTagMismatchException` rather than silently returning corrupt data.

### Where encryption happens

| Operation | Location |
| :-------- | :------- |
| Encrypt | `SecretsController.SetValues()` (Management API write path) |
| Decrypt | `ConsumerSecretsController.GetSecret()` (Consumer API read path) |

The raw ciphertext is **never returned** by the Management API. The Management API only returns metadata (version number, enabled flag, timestamps).

---

## API key lifecycle

Consumer API keys follow this lifecycle:

```text
CreateApplication()
       │
       ▼
RandomNumberGenerator.GetBytes(32)
       │
       ▼
Convert.ToHexString → "grm_{40 hex chars}"   ← plainApiKey (shown once)
       │
       ▼
IPasswordHasher<Application>.HashPassword()  ← PBKDF2 hash stored in DB
```

### Verification on every request

```text
X-Api-Key header
       │
       ▼
Load all Application rows from DB
       │
       ▼
foreach app:
  IPasswordHasher.VerifyHashedPassword(app.ApiKeyHash, rawKey)
  → match? → store app in HttpContext.Items → continue
  → no match? → 401
```

{: .note }
The linear scan across applications is acceptable at small scale. For deployments with hundreds of applications, consider adding a fast lookup index (e.g. storing the first 8 bytes of a SHA-256 hash as a lookup hint).

### Key rotation

```
POST /api/management/applications/{slug}/rotate-key
```

- Generates a new `grm_` prefixed key
- Replaces `ApiKeyHash` in the database
- Returns the new plain key **once**
- The old key is **immediately invalid**

There is no grace period. Schedule rotations during low-traffic windows or use a proxy that forwards requests with both old and new keys during the cutover.

---

## Middleware security

### `AdminApiKeyMiddleware`

- Intercepts **all** requests to `/api/management/*`
- Extracts the `Authorization: Bearer <token>` header
- Compares it against `Management:AdminApiKey` from configuration using `string.Equals(..., OrdinalIgnoreCase)`
- Returns `401` and short-circuits if missing or mismatched
- Passes through all other paths unchanged

### `ConsumerApiKeyMiddleware`

- Intercepts **all** requests to `/api/consumer/*`
- Extracts the `X-Api-Key` header
- Performs the hash verification loop described above
- Returns `401` and short-circuits on failure
- Attaches the matched `Application` entity to `HttpContext.Items`

---

## Security checklist for production

| Item | Recommended action |
| :--- | :----------------- |
| `Management:AdminApiKey` | Use a random 40+ character string; store in a secrets manager |
| `Encryption:MasterKey` | Use a random 32+ character string; back it up securely |
| SQLite file | Mount on an encrypted volume; restrict file permissions |
| HTTPS | Place Grimoire behind a TLS-terminating reverse proxy (nginx, Caddy, etc.) |
| CORS | Restrict `Cors:AllowedOrigins` to known frontend origins |
| Log files | Do not log request bodies (Serilog config does not enable this by default) |
