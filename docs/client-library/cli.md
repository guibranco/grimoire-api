---
title: CLI
parent: Client Library
nav_order: 4
permalink: /cli/
---

# ⌨️ `grimoire` CLI
{: .no_toc }

`Grimoire.Cli` is a .NET global tool for reading secrets, exporting configuration and running a program with its secrets injected as environment variables. It works for any application, .NET or not, and in scripts, CI jobs and systemd units.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
1. TOC
{:toc}
</details>

---

## Install

```bash
dotnet tool install -g Grimoire.Cli
grimoire --help
```

## Sign in

```bash
grimoire login --url https://grimoire.example.com --app my-app
```

`login` asks for a token without echoing it. When standard input is piped, it reads the token from there instead:

```bash
echo "$GRIMOIRE_TOKEN" | grimoire login --url https://grimoire.example.com
```

The token can be an application API key or an [access token]({{ site.baseurl }}/api-reference/authentication/). Before saving anything, `login` checks that Grimoire accepts it. The CLI saves the URL and token to `~/.grimoire/credentials.json`, which only you can read or write on Linux and macOS. `grimoire logout` deletes that file.

### Environment variables

| Variable | Use |
| :------- | :-- |
| `GRIMOIRE_URL` | API base URL; overrides the saved one |
| `GRIMOIRE_TOKEN` | Token; overrides the saved one. Use this in CI instead of `login` |
| `GRIMOIRE_ENVIRONMENT` | Default for `--environment` |
| `GRIMOIRE_CONFIG_DIR` | Where the credentials file is kept (default `~/.grimoire`) |

## Commands

Each command that reads values takes `-e, --environment <slug>`.

### `get`

Prints a secret's current value and nothing else, so it works in command substitution:

```bash
export SMTP_HOST="$(grimoire get smtp-host -e production)"
```

### `config list`

Prints the effective configuration (globals merged in) as sorted `Key=value` lines.

```bash
grimoire config list -e staging
```

### `export`

Writes every value the application can read in the environment, using the [export endpoint]({{ site.baseurl }}/api-reference/consumer-api/#export).

```bash
grimoire export -e local > .env                                # dotenv (default)
grimoire export -e local --format dotnet -o appsettings.Local.json
grimoire export -e production --include configurations --prefix Feature:
```

| Option | Description |
| :----- | :---------- |
| `-f, --format` | `env` (default), `json` or `dotnet` |
| `--include` | `secrets`, `configurations` or both, comma-separated |
| `--prefix` | Only keys and secret names starting with this, ignoring case |
| `-o, --output` | Write to a file instead of standard output. The file is created readable only by you |

### `run`

Starts a program with the environment's secrets and configuration as environment variables. Nothing is written to disk.

```bash
grimoire run -e local -- dotnet run
grimoire run -e production --prefix Smtp -- ./send-report.sh
```

- Variable names follow the `env` format: `Feature:DarkMode` becomes `FEATURE__DARKMODE`, which .NET reads back as `Feature:DarkMode`. `db-host` becomes `DB_HOST`.
- If a secret and a configuration entry end up with the same name, the secret wins.
- The program inherits the rest of the environment, but not `GRIMOIRE_TOKEN`.
- The program uses the terminal directly. Ctrl+C reaches it, and `grimoire` waits for it to exit and returns its exit code. If the program can't be started, `grimoire` exits with `127`.
- Secrets with no active version are skipped, with a note on standard error.

On a server, `run` keeps secrets out of unit files and `.env` files:

```ini
[Service]
Environment=GRIMOIRE_URL=https://grimoire.example.com
EnvironmentFile=/etc/grimoire/my-app.token   # GRIMOIRE_TOKEN=..., readable by root only
ExecStart=/usr/local/bin/grimoire run -e production -- /opt/my-app/my-app
```

## Exit codes

| Code | Meaning |
| :--- | :------ |
| `0` | Success (for `run`, the program's own exit code is returned instead) |
| `1` | Grimoire refused the request, could not be reached, or credentials or the environment are missing. The reason is printed to standard error |
| `127` | `run` could not start the program |

## Audit

`get` is recorded as `secret.read` and `export` and `run` as `export.read`, under the token that made the call. Values are never recorded.
