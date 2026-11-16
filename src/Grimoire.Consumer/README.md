# Grimoire.Consumer

.NET client for [Grimoire](https://github.com/guibranco/grimoire-api), the self-hosted Vault & Configuration Manager.

- **Configuration provider**: load every configuration entry for an environment into `IConfiguration` with one call.
- **Secret client**: fetch individual versioned secrets on demand.
- **Azure-compatible shapes**: the consumer API mirrors Azure Key Vault and Azure App Configuration responses.

## Install

```bash
dotnet add package Grimoire.Consumer
```

## Load configuration into `IConfiguration`

```csharp
using Grimoire.Consumer;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddGrimoire(
    baseUrl:     "https://grimoire.example.com",
    apiKey:      builder.Configuration["Grimoire:ApiKey"]!,
    environment: "production");

var dbHost = builder.Configuration["Database:Host"];
```

Entries are loaded once at startup and work with `IOptions<T>` as usual.

## Fetch a secret

```csharp
using Grimoire.Consumer;

var secrets = new GrimoireSecretClient(
    "https://grimoire.example.com", apiKey, "production");

var secret = await secrets.GetSecretAsync("DatabasePassword");
Console.WriteLine(secret.Value);
```

## Documentation

Full docs, including the server setup and API reference: <https://guibranco.github.io/grimoire-api/>

## License

MIT
