# Grimoire CLI

Command-line client for [Grimoire](https://github.com/guibranco/grimoire-api), the self-hosted Vault & Configuration Manager.

```bash
dotnet tool install -g Grimoire.Cli

grimoire login --url https://grimoire.example.com     # prompts for the token
grimoire get smtp-host -e production
grimoire config list -e staging
grimoire export -e local --format env > .env
grimoire run -e local -- dotnet run                   # secrets as env vars, nothing on disk
grimoire diff staging production
grimoire set Feature:DarkMode true -e staging         # after: grimoire login --admin
grimoire import .env -e local
```

See the [CLI documentation](https://guibranco.github.io/grimoire-api/cli/) for every command and option.
