---
title: CLI
parent: Client Library
nav_order: 4
permalink: /cli/
---

# ⌨️ `grimoire` CLI
{: .no_toc }

`Grimoire.Cli` is a .NET global tool for reading and writing secrets and configuration, exporting them, comparing environments and running a program with its secrets injected as environment variables. It works for any application, .NET or not, and in scripts, CI jobs and systemd units.

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

### Admin key, for writes

`set` and `import` write through the Management API, which needs the admin key instead of a token. Save it once, for the application you'll write to:

```bash
grimoire login --url https://grimoire.example.com --app my-app --admin
```

This asks for the admin key (or reads it from piped standard input), checks it against the application, and saves it next to the token. Run `login` once for each to keep both. The admin key can manage every application, so prefer `GRIMOIRE_ADMIN_KEY` in CI over saving it on shared machines.

### Environment variables

| Variable | Use |
| :------- | :-- |
| `GRIMOIRE_URL` | API base URL; overrides the saved one |
| `GRIMOIRE_TOKEN` | Token; overrides the saved one. Use this in CI instead of `login` |
| `GRIMOIRE_ADMIN_KEY` | Admin key for `set` and `import`; overrides the saved one |
| `GRIMOIRE_APP` | Application slug for `set` and `import`; overrides the saved one |
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
| `--structured` | How `array` and `json` values are written in `env` and `dotnet`: `raw` (default, the JSON text), `flatten` or `both`. See [value types](#value-types) |
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
- The program inherits the rest of the environment, but not `GRIMOIRE_TOKEN` or `GRIMOIRE_ADMIN_KEY`.
- The program uses the terminal directly. Ctrl+C reaches it, and `grimoire` waits for it to exit and returns its exit code. If the program can't be started, `grimoire` exits with `127`.
- `--structured`, `--include` and `--prefix` work as they do for `export`.
- Secrets with no active version are skipped, with a note on standard error.

On a server, `run` keeps secrets out of unit files and `.env` files:

```ini
[Service]
Environment=GRIMOIRE_URL=https://grimoire.example.com
EnvironmentFile=/etc/grimoire/my-app.token   # GRIMOIRE_TOKEN=..., readable by root only
ExecStart=/usr/local/bin/grimoire run -e production -- /opt/my-app/my-app
```

### `diff`

Shows what differs between two environments, using the read token:

```bash
grimoire diff staging production
```

```
--- staging
+++ production
~ config Feature:DarkMode: false → true
+ config Smtp:Host=mail.example.com
- secret legacy-api-key
3 differences.
```

- Configuration keys are compared by value. Line breaks in values are shown as `\n`, so each difference stays on one line.
- Secrets are compared only by whether each environment has an active version. Their values are never fetched for the comparison or shown.
- `--include` and `--prefix` narrow the comparison, as for `export`.
- `--exit-code` makes `diff` exit with `1` when the environments differ, for CI checks.

### `set`

Creates or updates a configuration entry, or writes a new version of a secret. Needs the [admin key](#admin-key-for-writes).

```bash
grimoire set Feature:DarkMode true -e staging
grimoire set AllowedIps -e production --type array < ips.txt     # one item per line, or a JSON array
grimoire set Smtp -e production --type json '{"Host":"mail","Port":25}'
grimoire set smtp-password -e production --secret                  # prompts without echoing
vault-tool read smtp | grimoire set smtp-password -e production --secret
```

- A configuration value comes from the argument, or from standard input when the argument is left out. One trailing line break is dropped.
- `--type` is `string`, `multiline`, `array` or `json`. Leave it out to keep an existing entry's type; a new entry is a `string`.
- A secret's value is never taken from the command line, so it stays out of your shell history. It is read from standard input when piped, otherwise from a hidden prompt. The secret is created if it doesn't exist yet.
- `--app` picks the application when it isn't the saved one.

### `import`

Imports a `.env`, `appsettings.json`, JSON or PHP config file through the [import endpoint]({{ site.baseurl }}/api-reference/management-api/#import). Needs the [admin key](#admin-key-for-writes).

```bash
grimoire import .env -e local                 # shows the plan, then asks before applying
grimoire import appsettings.Production.json -e production --overwrite --yes
grimoire import config.php -e staging --dry-run
cat .env | grimoire import - -e local --format env --yes
```

| Option | Description |
| :----- | :---------- |
| `-f, --format` | `env`, `appsettings`, `json` or `php`. Detected from the file name when left out |
| `--secret-keys` | Comma-separated name patterns to import as secrets (e.g. `*PASSWORD*,*_KEY`). Defaults to the server's list |
| `--exclude` | Comma-separated keys to leave out |
| `--overwrite` | Replace values that already exist. By default they are kept |
| `--dry-run` | Show the plan and stop |
| `-y, --yes` | Apply without asking. Required when standard input isn't a terminal, including `import -` |

The plan lists each key with its action (`create`, `update`, `skip`, `conflict`, `excluded`) and kind, but never values.

## Value types

[Configuration value types]({{ site.baseurl }}/api-reference/management-api/#value-types) `array` and `json` are stored as JSON text. How they come out depends on the format:

- **`export --format json`** writes them as real JSON arrays and objects, so the file round-trips through `import`.
- **`env`, `dotnet` and `run`** write the JSON text under the key by default (`--structured raw`), matching the GitHub Action.
  - `--structured flatten` writes one entry per item instead, the way `IConfiguration` reads JSON. Arrays are indexed from 0, nested objects are joined with `:`, and numbers and booleans keep their JSON text. An empty array or object gives no entries. So `AllowedIps` becomes `ALLOWEDIPS__0`, `ALLOWEDIPS__1` in `env`, and `AllowedIps:0` in `dotnet`.
  - `--structured both` writes the JSON text and the flattened entries.

A value that isn't valid JSON is written as text, with a warning on standard error.

## Exit codes

| Code | Meaning |
| :--- | :------ |
| `0` | Success (for `run`, the program's own exit code is returned instead) |
| `1` | Grimoire refused the request, could not be reached, or credentials or the environment are missing; the reason is printed to standard error. With `diff --exit-code`, also: the environments differ |
| `127` | `run` could not start the program |

## Audit

`get` is recorded as `secret.read`, and `export` and `run` as `export.read`, under the token that made the call. `diff` reads through the same endpoints, so it is recorded as `export.read` and `secret.list`. `set` is recorded as a configuration or secret change and `import` as `import.apply`, by the admin key. Values are never recorded.
