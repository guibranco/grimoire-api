---
title: Configuration
parent: Getting Started
nav_order: 2
---

# ⚙️ Configuration
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Configuration sources

Grimoire uses the standard ASP.NET Core configuration pipeline. Settings are read in this priority order (later sources override earlier ones):

1. `appsettings.json` (lowest priority)
2. `appsettings.{Environment}.json` (e.g. `appsettings.Development.json`)
3. Environment variables
4. Command-line arguments (highest priority)

For Docker deployments, environment variables are the recommended approach. Use `__` (double underscore) as the section separator.

---

## All settings

### Connection strings

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `ConnectionStrings:Default` | `ConnectionStrings__Default` | `Data Source=grimoire.db` | SQLite connection string. Point `/data/grimoire.db` in Docker. |

### Management

| Setting | Env var | Default | Required | Description |
| :------ | :------ | :------ | :------: | :---------- |
| `Management:AdminApiKey` | `Management__AdminApiKey` | *(none)* | ✅ | Bearer token for all Management API endpoints. Must match the `Authorization: Bearer <key>` header. |

### Encryption

| Setting | Env var | Default | Required | Description |
| :------ | :------ | :------ | :------: | :---------- |
| `Encryption:MasterKey` | `Encryption__MasterKey` | *(none)* | ✅ | Master secret used as HKDF input to derive the AES-256 encryption key. Must be at least 32 characters. |

{: .warning }
**Changing the master key makes all existing secrets unreadable.** Back up your database and key before rotation.

### Backup

Encrypted, scheduled backups of the database to a local directory and/or S3-compatible storage. Off by default. All `Backup:*` settings, the restore commands and the runbook are in [Backup and restore](../operations/backup-and-restore.md).

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `Backup:Enabled` | `Backup__Enabled` | `false` | Turns on scheduled backups and `/management/backups` |
| `Backup:Key` | `Backup__Key` | *(none)* | Backup encryption passphrase (≥ 32 chars, different from the master key) |

### CORS

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `__1`, … | `[]` | List of allowed CORS origins. Use array notation for multiple values. |

**Example — multiple origins via environment variables:**

```bash
Cors__AllowedOrigins__0=https://app.example.com
Cors__AllowedOrigins__1=https://admin.example.com
```

### Dashboard

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `Dashboard:ExpiringWithinDays` | `Dashboard__ExpiringWithinDays` | `14` | Active secret versions expiring within this many days are flagged `secret-expiring`. Overridable per request with `?expiringWithinDays=`. |
| `Dashboard:StaleAfterDays` | `Dashboard__StaleAfterDays` | `7` | Consumers not seen for more than this many days are flagged `consumer-stale`. Overridable per request with `?staleAfterDays=`. |
| `Dashboard:CacheSeconds` | `Dashboard__CacheSeconds` | `15` | How long a computed summary is reused in memory. `0` disables caching. |

### Serilog (logging)

| Setting | Default | Description |
| :------ | :------ | :---------- |
| `Serilog:MinimumLevel:Default` | `Information` | Global minimum log level |
| `Serilog:MinimumLevel:Override:Microsoft` | `Warning` | Suppresses noisy framework logs |
| `Serilog:MinimumLevel:Override:System` | `Warning` | Suppresses noisy system logs |

Log files are written to `logs/grimoire-YYYYMMDD.log` with daily rolling. Add `-v grimoire-logs:/app/logs` to your Docker run command to persist them.

### LogStream (centralised logging)

Logs can also be shipped to a [logstream-server](https://github.com/guibranco/logstream-server), through the [`Serilog.Sinks.LogStream`](https://www.nuget.org/packages/Serilog.Sinks.LogStream) sink. Shipping is enabled only when `Server`, `ApiKey` and `ApiToken` are all set; otherwise nothing changes.

| Setting | Default | Description |
| :------ | :------ | :---------- |
| `LogStream:Enabled` | `true` | Kill switch — `false` stops all LogStream traffic (e.g. while the server is down) |
| `LogStream:Server` | — | Base URL of the logstream-server (`/api/logs` is appended; a URL already ending in it is accepted too) |
| `LogStream:ApiKey` | — | The client's app_key, sent as `X-Api-Key` |
| `LogStream:ApiToken` | — | The client's token, sent as `X-Api-Token` |
| `LogStream:AppId` | running version | Stored as `app_id` |
| `LogStream:MinimumLevel` | `Information` | Lowest level shipped (console/file logs are unaffected) |
| `LogStream:Transport` | `Udp` | `Udp`: one HMAC-signed, fire-and-forget datagram per event (events too large for a datagram go over HTTPS). `Http`: batched POSTs with retries |
| `LogStream:UdpHost` | host of `Server` | Host receiving the datagrams — use `127.0.0.1` when Grimoire runs on the LogStream host |
| `LogStream:UdpPort` | `8082` | The server's `UDP_PORT` |

Over UDP, delivery is fire-and-forget: the server never answers, so rejected packets (bad signature, clock skew, a source IP not allowed on the UDP port) are only visible in `udp.rejected` on the server's `GET /api/health`. Keep the clock NTP-synced. Set `LogStream:Transport=Http` to go back to HTTPS with retries.

With `Http`, every Serilog event is sent in background batches (up to 100 events every 2 s) with its rendered message, message template, source context as `category`, properties as `context`, the exception (type, message, stack trace, inner exceptions) in `context.exception`, and the request's W3C trace id (32 hex characters) as `trace_id`, so all entries of one request can be found together. A slow or unreachable LogStream server never blocks requests: at most 10,000 events wait in memory, transient failures (network errors, timeouts, HTTP 408, 429 and 5xx) are retried with backoff for up to 5 minutes, then dropped, and other rejections are dropped at once. The sink reports its own problems through Serilog's `SelfLog`.

```bash
LogStream__Server=https://logs.example.com
LogStream__ApiKey=grimoire-api
LogStream__ApiToken=<token>
```

---

## Complete `appsettings.json` reference

```json
{
  "ConnectionStrings": {
    "Default": "Data Source=grimoire.db"
  },
  "Management": {
    "AdminApiKey": "change-me"
  },
  "Encryption": {
    "MasterKey": "change-me-32-chars-minimum-key!!"
  },
  "Cors": {
    "AllowedOrigins": [
      "http://localhost:5173"
    ]
  },
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "AllowedHosts": "*"
}
```

---

## Environment-specific files

Create `appsettings.Production.json` to override values for production without changing the base file:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Warning"
    }
  }
}
```

Set `ASPNETCORE_ENVIRONMENT=Production` to activate it.

---

## Docker Compose example

```yaml
services:
  api:
    image: grimoire-api:latest
    ports:
      - "8080:8080"
    environment:
      ASPNETCORE_ENVIRONMENT: Production
      ConnectionStrings__Default: "Data Source=/data/grimoire.db"
      Management__AdminApiKey: "${GRIMOIRE_ADMIN_KEY}"
      Encryption__MasterKey: "${GRIMOIRE_MASTER_KEY}"
      Cors__AllowedOrigins__0: "https://your-frontend.example.com"
    volumes:
      - grimoire-data:/data
    restart: unless-stopped

volumes:
  grimoire-data:
```

Store the actual secret values in a `.env` file (gitignored) or use Docker secrets / a secrets manager.
