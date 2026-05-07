---
title: Installation
parent: Getting Started
nav_order: 1
---

# 📦 Installation
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Docker Compose (recommended)

The fastest path to a running instance. No .NET SDK required.

### 1. Clone the repository

```bash
git clone https://github.com/guibranco/grimoire-api.git
cd grimoire-api
```

### 2. Configure secrets

{: .warning }
Never use the default values in production. Change the keys below before you start.

Copy or edit `docker-compose.yml` to set your own values:

```yaml
environment:
  Management__AdminApiKey: "your-strong-admin-bearer-token"
  Encryption__MasterKey:   "your-32-char-minimum-master-key!!"
```

Or export them as shell variables (they override the compose file):

```bash
export Management__AdminApiKey="your-strong-admin-bearer-token"
export Encryption__MasterKey="your-32-char-minimum-master-key!!"
```

### 3. Start the stack

```bash
docker compose up -d
```

### 4. Verify

```bash
curl http://localhost:8080/health
# → 200 OK

curl http://localhost:8080/swagger
# → Swagger UI HTML
```

The SQLite database is persisted in the `grimoire-data` Docker volume.

---

## Docker (without Compose)

Build and run the image directly:

```bash
# Build
docker build -t grimoire-api:latest .

# Run
docker run -d \
  -p 8080:8080 \
  -e Management__AdminApiKey="your-admin-key" \
  -e Encryption__MasterKey="your-32-char-minimum-master-key!!" \
  -v grimoire-data:/data \
  grimoire-api:latest
```

---

## dotnet run (development)

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)

### Steps

```bash
git clone https://github.com/guibranco/grimoire-api.git
cd grimoire-api

dotnet run --project src/Grimoire.Api \
  --Management:AdminApiKey="your-admin-key" \
  --Encryption:MasterKey="your-32-char-minimum-master-key!!"
```

The API starts on `http://localhost:5000` by default (or the port configured by `ASPNETCORE_URLS`).

---

## Building from source

```bash
git clone https://github.com/guibranco/grimoire-api.git
cd grimoire-api

# Restore and build all projects
dotnet restore
dotnet build --configuration Release

# Run database migrations (creates grimoire.db)
dotnet ef database update \
  --project src/Grimoire.Infrastructure \
  --startup-project src/Grimoire.Api

# Start the API
dotnet run --project src/Grimoire.Api --configuration Release
```

---

## Health check

All deployment methods expose a health endpoint:

```
GET /health  →  200 OK
```

This is used by Docker's built-in health check and by the Testcontainers wait strategy in E2E tests.

---

## Ports and volumes

| Item | Default | Description |
| :--- | :------ | :---------- |
| HTTP port | `8080` | API and Swagger UI |
| Data volume | `/data` (in container) | SQLite database location |
| DB file | `/data/grimoire.db` | Automatically created on first start |
