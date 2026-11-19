---
title: Backup and restore
parent: Operations
nav_order: 1
---

# 💾 Backup and restore
{: .no_toc }

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

Grimoire is the single source of truth for every secret it holds. Losing the SQLite file, **or the master key**, loses all of them. This page covers how backups work, how to set them up, and the runbook for getting Grimoire back.

## How it works

| Step | What happens |
| :--- | :----------- |
| **Snapshot** | `VACUUM INTO` takes a consistent online copy of the database. Reads and writes keep working while it runs. |
| **Compress + encrypt** | The copy is gzip-compressed and encrypted with AES-256-GCM, using a key derived (HKDF-SHA256) from `Backup:Key`. |
| **Store** | The encrypted `.gbak` file is written to every enabled target: a local directory, an S3-compatible bucket (OCI Object Storage, AWS S3, MinIO, R2…), or both. |
| **Retention** | Each target keeps the newest `Backup:RetentionCount` backups. Older ones are deleted. Files that are not Grimoire backups are never touched. |
| **Monitoring** | Every backup pings `Backup:HealthcheckUrl` (healthchecks.io convention: `/fail` on failure). A backup that misses any target counts as failed. |
| **Restore test** | `--verify-backup latest` decrypts the newest backup into a throwaway copy, checks SQLite integrity, counts applications and secret versions, and decrypts one secret with the master key. On vinhedo1 it runs daily from a systemd timer and pings `Backup:VerifyHealthcheckUrl`. |

Backups run on the `Backup:Schedule` cron (default `0 3 * * *`, daily at 03:00 UTC) and on demand with `POST /management/backups`.

### Two keys, both required

A backup holds the database as-is, so secret values inside it are **still encrypted with the master key**. To restore you need:

1. **`Backup:Key`** to decrypt the backup file, and
2. **`Encryption:MasterKey`** for Grimoire to read the secret values in it.

The two keys must be different; Grimoire refuses to start if they are the same. A leaked backup file plus the backup key still does not expose secret values.

### File format

```text
"GRIMBAK\0" (8) | format version (1) | nonce (12) | tag (16) | AES-256-GCM( gzip( sqlite file ) )
```

The 9-byte header is authenticated as associated data. File names are `grimoire-yyyyMMddTHHmmssfffZ.gbak` (UTC), so they sort chronologically.

## Configuration

| Setting | Env var | Default | Description |
| :------ | :------ | :------ | :---------- |
| `Backup:Enabled` | `Backup__Enabled` | `false` | Turns on the schedule and the `/management/backups` endpoints |
| `Backup:Key` | `Backup__Key` | *(none)* | Backup encryption passphrase. ≥ 32 chars, not `change-me…`, different from the master key |
| `Backup:Schedule` | `Backup__Schedule` | `0 3 * * *` | Five-field cron. Empty disables scheduled runs (on-demand still works) |
| `Backup:TimeZone` | `Backup__TimeZone` | `UTC` | Time zone the cron is evaluated in |
| `Backup:RetentionCount` | `Backup__RetentionCount` | `14` | Backups kept per target |
| `Backup:HealthcheckUrl` | `Backup__HealthcheckUrl` | *(none)* | Pinged after every backup |
| `Backup:VerifyHealthcheckUrl` | `Backup__VerifyHealthcheckUrl` | *(none)* | Pinged after every restore test |
| `Backup:Local:Enabled` | `Backup__Local__Enabled` | `false` | Write to a local directory |
| `Backup:Local:Directory` | `Backup__Local__Directory` | *(none)* | Created with mode `700`; files are `600` |
| `Backup:S3:Enabled` | `Backup__S3__Enabled` | `false` | Write to an S3-compatible bucket |
| `Backup:S3:ServiceUrl` | `Backup__S3__ServiceUrl` | *(none)* | e.g. `https://<namespace>.compat.objectstorage.<region>.oraclecloud.com` |
| `Backup:S3:Region` | `Backup__S3__Region` | *(none)* | Signing region, e.g. `sa-saopaulo-1` |
| `Backup:S3:Bucket` | `Backup__S3__Bucket` | *(none)* | Bucket name |
| `Backup:S3:Prefix` | `Backup__S3__Prefix` | `grimoire/` | Object key prefix |
| `Backup:S3:AccessKey` / `SecretKey` | `Backup__S3__AccessKey` / `Backup__S3__SecretKey` | *(none)* | For OCI, a [Customer Secret Key](https://docs.oracle.com/en-us/iaas/Content/Identity/Tasks/managingcredentials.htm#create-secret-key) |
| `Backup:S3:ForcePathStyle` | `Backup__S3__ForcePathStyle` | `true` | Path-style URLs (needed by OCI and MinIO) |

When `Backup:Enabled` is `true`, Grimoire checks these settings at startup and refuses to start if the key is weak or no target is configured.

{: .note }
The local directory alone does not survive losing the server. Use it as a fast first copy, and keep S3 enabled for the off-site copy.

## Command-line modes

`Grimoire.Api.dll` has three maintenance modes. They read the normal configuration (appsettings, environment variables) and never start the web server:

```bash
dotnet Grimoire.Api.dll --backup                              # take a backup now
dotnet Grimoire.Api.dll --verify-backup <file|latest>         # restore test into a throwaway copy
dotnet Grimoire.Api.dll --restore <file|latest> [--force]     # replace the database
```

- `latest` picks the newest backup across enabled targets, reading the local directory first.
- `--restore` refuses to overwrite an existing database unless `--force` is passed. The replaced file (and its `-wal`/`-shm`) is kept as `grimoire.db.pre-restore-<timestamp>`.
- **Stop the service before `--restore`.** Pending migrations are applied when the service next starts.
- Exit code `0` means success, `1` failure, `2` bad arguments.

On vinhedo1, use the `grimoire-backup` wrapper below. It runs these modes as the service user, with the service's own environment.

## Setting up backups on vinhedo1

### 1. Create the bucket and credentials (OCI)

1. Create a **private** Object Storage bucket, e.g. `grimoire-backups`, in a different region from vinhedo1 if you can.
2. Optional: add an Object Lifecycle rule as a safety net, e.g. delete objects older than 60 days. Grimoire's retention does the normal pruning.
3. Create a dedicated IAM user whose policy only allows `manage objects` in that bucket, then generate a **Customer Secret Key** for it. That gives the access key and secret key.
4. Note the S3-compatible endpoint: `https://<namespace>.compat.objectstorage.<region>.oraclecloud.com`.

### 2. Create two checks on healthchecks.io

- **grimoire-backup**: period 1 day, grace 2 hours
- **grimoire-backup-verify**: period 1 day, grace 2 hours

### 3. Generate the backup key

```bash
openssl rand -base64 48
```

Put it in your password manager **now**, before Grimoire uses it (see [Key escrow](#key-escrow)).

### 4. Write `/etc/grimoire/backup.env`

```ini
Backup__Enabled=true
Backup__Key=<the key from step 3>
Backup__RetentionCount=14
Backup__HealthcheckUrl=https://hc-ping.com/<uuid-backup>
Backup__VerifyHealthcheckUrl=https://hc-ping.com/<uuid-verify>
Backup__Local__Enabled=true
Backup__Local__Directory=/var/lib/grimoire/snapshots
Backup__S3__Enabled=true
Backup__S3__ServiceUrl=https://<namespace>.compat.objectstorage.<region>.oraclecloud.com
Backup__S3__Region=<region>
Backup__S3__Bucket=grimoire-backups
Backup__S3__Prefix=vinhedo1/
Backup__S3__AccessKey=<access key>
Backup__S3__SecretKey=<secret key>
```

```bash
sudo chown root:grimoire /etc/grimoire/backup.env
sudo chmod 640 /etc/grimoire/backup.env
```

{: .note }
`/var/lib/grimoire/snapshots` is separate from `/var/lib/grimoire/backups`, which `grimoire-deploy` uses for its pre-deploy rollback copies.

### 5. Install the wrapper, drop-in and timer

Re-run `setup-server.sh` (see the header of `deploy/vinhedo1/setup-server.sh`; omit the key file to keep the current deploy key). It installs:

- `/usr/local/bin/grimoire-backup`, the operator wrapper
- `grimoire-api.service.d/backup.conf`, which loads `/etc/grimoire/backup.env` into the service
- `grimoire-backup-verify.service` and `.timer`, the daily restore test at 05:00 UTC, enabled because `backup.env` now exists

```bash
sudo systemctl restart grimoire-api
sudo grimoire-backup now        # first backup: local + S3, pings healthchecks.io
sudo grimoire-backup verify     # first restore test
systemctl list-timers grimoire-backup-verify.timer
```

## Restore runbook

### A. Restore on the same server (bad change, corrupted database)

```bash
# Newest backup (local directory first, then S3):
sudo grimoire-backup restore latest

# Or a specific one:
ls -l /var/lib/grimoire/snapshots
sudo grimoire-backup restore /var/lib/grimoire/snapshots/grimoire-20261119T030000001Z.gbak
```

The wrapper asks for confirmation, stops `grimoire-api`, restores (keeping the old file as `grimoire.db.pre-restore-*`), starts the service and waits for `/health`.

### B. vinhedo1 is gone

1. Provision a new host and install Grimoire with the usual setup (`setup-server.sh`, then a deploy from GitHub Actions). Do not create any data.
2. Take **both** keys from escrow. Recreate the service environment with the **same** `Encryption__MasterKey`, then write `/etc/grimoire/backup.env` as in step 4. Local can stay enabled; S3 is what gets read.
3. Restore from S3 and start:

   ```bash
   sudo grimoire-backup restore latest
   ```

4. Re-run `setup-server.sh` now that `backup.env` exists, so it enables the daily restore test (or run `sudo systemctl enable --now grimoire-backup-verify.timer`).
5. Check that applications and secrets are there, in the UI or with `sudo grimoire-backup verify`.
6. Point DNS (`api.grimoire.straccini.com`) at the new host.
7. Consumers keep their API keys: the key hashes are in the restored database.

### C. No Grimoire binary at hand

From any machine with the .NET 10 SDK and a copy of the `.gbak` file:

```bash
git clone https://github.com/guibranco/grimoire-api && cd grimoire-api
export Backup__Key='<backup key>' Encryption__MasterKey='<master key>' \
       ConnectionStrings__Default='Data Source=/tmp/restored/grimoire.db'
dotnet run --project src/Grimoire.Api -- --restore /path/to/grimoire-….gbak
```

## Key escrow

Grimoire cannot hold its own keys: if vinhedo1 is lost, so is anything stored only there. Keep these **outside** Grimoire and outside vinhedo1:

| Item | Where it lives day to day | Escrow |
| :--- | :------------------------ | :----- |
| `Encryption:MasterKey` | grimoire-api service environment on vinhedo1 | Password manager entry "Grimoire – master key" **and** an offline copy (printed or on an encrypted USB drive) |
| `Backup:Key` | `/etc/grimoire/backup.env` on vinhedo1 | Same password-manager entry and offline copy as the master key |
| OCI Customer Secret Key | `/etc/grimoire/backup.env` | Can be re-issued from the OCI console; escrow optional |
| `Management:AdminApiKey` | service environment | Can be replaced when rebuilding; escrow optional |

Rules:

- **Rotating either key:** take a fresh backup straight after the change, and update escrow **before** relying on it. Backups taken under an old `Backup:Key` need the old key; keep it until those backups have aged out of retention (14 days by default).
- **Changing `Encryption:MasterKey` makes existing secrets unreadable**, in the live database and in every backup. Until envelope encryption and master-key rotation exist ([#80](https://github.com/guibranco/grimoire-api/issues/80)), don't change it.
- Check once a quarter that the escrowed keys open the latest backup: run `--verify-backup latest` on a laptop with the keys taken from escrow, not from vinhedo1.
