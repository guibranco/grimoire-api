---
title: Client Library
nav_order: 5
has_children: true
permalink: /client-library/
---

# 🧩 Client Library

`Grimoire.Consumer` is a standalone .NET 10 library for consuming the Grimoire API from within your applications.

---

## In this section

| Page | What you'll learn |
| :--- | :---------------- |
| [Configuration Provider]({{ site.baseurl }}/client-library/configuration-provider/) | Inject Grimoire configs as `IConfiguration` |
| [Secret Client]({{ site.baseurl }}/client-library/secret-client/) | Fetch individual secrets programmatically |

---

## Overview

The library provides two ways to consume Grimoire:

| Class | Use when |
| :---- | :------- |
| `GrimoireConfigurationClient` | You want configs (and optionally secrets) to be part of `IConfiguration` |
| `GrimoireSecretClient` | You need fine-grained control — fetch a secret at a specific point in code |

Both are in the `Grimoire.Consumer` namespace. The library has no dependencies on the Grimoire server-side projects.

---

## Adding the library

Reference the project directly (until a NuGet package is published):

```xml
<!-- In your app's .csproj -->
<ProjectReference Include="path/to/Grimoire.Consumer/Grimoire.Consumer.csproj" />
```

---

## Azure compatibility

The consumer API response shapes match **Azure Key Vault** and **Azure App Configuration**, so you can point the client library at either Grimoire or Azure without changing your application code.
