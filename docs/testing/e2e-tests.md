---
title: E2E Tests
parent: Testing
nav_order: 3
---

# 🐳 End-to-End Tests
{: .no_toc }

Full end-to-end tests that build and run the real Docker image and exercise the API over HTTP from the outside — no in-process shortcuts.

<details open markdown="block">
  <summary>Table of contents</summary>
  {: .text-delta }
- TOC
{:toc}
</details>

---

## Requirements

- Docker Desktop (or Docker Engine on Linux)
- .NET 10 SDK

---

## Running

### Option A — build the image automatically (default)

```bash
dotnet test tests/Grimoire.E2eTests
```

The test fixture builds the Docker image from the solution root `Dockerfile` on first run. This adds ~30–60 seconds on a cold cache.

### Option B — pre-build the image (faster in CI)

```bash
# Build once
docker build -t grimoire-api:e2e .

# Point the tests at the pre-built image
GRIMOIRE_TEST_IMAGE=grimoire-api:e2e dotnet test tests/Grimoire.E2eTests
```

Setting `GRIMOIRE_TEST_IMAGE` skips the image-build step entirely.

---

## How it works

E2E tests use [Testcontainers for .NET](https://dotnet.testcontainers.org/) (`DotNet.Testcontainers`).

### `GrimoireApiFixture`

```csharp
public sealed class GrimoireApiFixture : IAsyncLifetime
{
    public async Task InitializeAsync()
    {
        var imageName = Environment.GetEnvironmentVariable("GRIMOIRE_TEST_IMAGE");

        if (string.IsNullOrEmpty(imageName))
        {
            // Build the image from the solution Dockerfile
            _builtImage = new ImageFromDockerfileBuilder()
                .WithDockerfileDirectory(CommonDirectoryPath.GetSolutionDirectory(), string.Empty)
                .WithDockerfile("Dockerfile")
                .WithName($"grimoire-api:e2e-{Guid.NewGuid():N}"[..30])
                .Build();
            await _builtImage.CreateAsync();
            imageName = _builtImage.FullName;
        }

        _container = new ContainerBuilder()
            .WithImage(imageName)
            .WithPortBinding(8080, true)         // random host port
            .WithEnvironment("ASPNETCORE_ENVIRONMENT",  "Development")
            .WithEnvironment("Management__AdminApiKey",  AdminKey)
            .WithEnvironment("Encryption__MasterKey",    MasterKey)
            .WithEnvironment("Cors__AllowedOrigins__0",  "http://localhost:5173")
            .WithWaitStrategy(Wait.ForUnixContainer()
                .UntilHttpRequestIsSucceeded(r => r.ForPath("/health").ForPort(8080)))
            .Build();

        await _container.StartAsync();
        BaseUrl = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(8080)}";
    }
}
```

Key points:

- **Random host port** — `WithPortBinding(8080, true)` lets Docker assign a free port, so tests don't conflict with each other or with a locally running API
- **Health-check wait** — the fixture waits until `GET /health` returns `200` before running any tests
- **Isolated environment** — each test run uses fresh in-container state (no persistent volume)

---

## Test coverage (6 tests)

| Test | What it verifies |
| :--- | :--------------- |
| `HealthEndpoint_Returns200` | Container started successfully; `/health` is reachable |
| `FullLifecycle_CreateApp_SetSecret_ConsumeSecret` | Create app → create secret → set value → consumer reads decrypted value |
| `FullLifecycle_CreateApp_SetConfig_ConsumeConfig` | Create app → create config → consumer reads value with label |
| `ManagementApi_RequiresAuthentication` | Unauthenticated management request returns `401` |
| `ConsumerApi_RequiresApiKey` | Unauthenticated consumer request returns `401` |
| `RotateKey_OldKeyInvalid_NewKeyWorks` | Post-rotation: old key `401`, new key `200` |

---

## CI integration

In the GitHub Actions `e2e` job, the image is built before the tests run:

```yaml
- name: Build Docker image
  run: docker build -t grimoire-api:e2e .

- name: Run E2E tests
  env:
    GRIMOIRE_TEST_IMAGE: grimoire-api:e2e
  run: dotnet test tests/Grimoire.E2eTests --configuration Release
```

This is equivalent to Option B above — one `docker build`, then the tests reuse the named image via the env var.

---

## Debugging failures

If a test fails, Testcontainers does not automatically remove the container. Find it with:

```bash
docker ps -a | grep grimoire
```

Inspect the logs:

```bash
docker logs <container-id>
```

The container's SQLite database lives in `/data/grimoire.db` inside the container. You can copy it out:

```bash
docker cp <container-id>:/data/grimoire.db ./debug.db
```

---

## Project structure

```text
tests/Grimoire.E2eTests/
├── Grimoire.E2eTests.csproj   ← net10.0, Testcontainers 3.*, no project refs
├── GrimoireApiFixture.cs       ← IAsyncLifetime fixture, builds/starts container
└── E2eTests.cs                 ← 6 test methods
```

{: .note }
The E2E test project has **no `ProjectReference`** to any Grimoire source project. It communicates with the API entirely over HTTP. This ensures that the tests verify the production Docker image, not the dev build.
