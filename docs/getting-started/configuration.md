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

### CORS

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `Cors:AllowedOrigins` | `Cors__AllowedOrigins__0`, `__1`, … | `[]` | List of allowed CORS origins. Use array notation for multiple values. |

**Example — multiple origins via environment variables:**

```bash
Cors__AllowedOrigins__0=https://app.example.com
Cors__AllowedOrigins__1=https://admin.example.com
```

### Serilog (logging)

| Setting | Default | Description |
| :------ | :------ | :---------- |
| `Serilog:MinimumLevel:Default` | `Information` | Global minimum log level |
| `Serilog:MinimumLevel:Override:Microsoft` | `Warning` | Suppresses noisy framework logs |
| `Serilog:MinimumLevel:Override:System` | `Warning` | Suppresses noisy system logs |

Log files are written to `logs/grimoire-YYYYMMDD.log` with daily rolling. Add `-v grimoire-logs:/app/logs` to your Docker run command to persist them.

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
