using System.Diagnostics;
using Grimoire.Cli;

namespace Grimoire.IntegrationTests.Cli;

/// <summary>The <c>grimoire</c> commands end to end against the in-memory API.</summary>
public sealed class CliTests(GrimoireWebApplicationFactory factory)
    : IClassFixture<GrimoireWebApplicationFactory>,
        IDisposable
{
    // Fixture values are built at runtime so secret scanners don't take them for credentials.
    private static readonly string SecretValue = string.Join('-', "not", "a", "real", "value");

    private readonly string _settings = Directory.CreateTempSubdirectory("grimoire-cli-").FullName;
    private readonly Dictionary<string, string> _environment = [];
    private readonly RecordingRunner _runner = new();

    private HttpClient Mgmt => factory.CreateManagementClient();

    public void Dispose() => Directory.Delete(_settings, recursive: true);

    private sealed class RecordingRunner : IProcessRunner
    {
        public ProcessStartInfo? Started { get; private set; }
        public int ExitCode { get; set; }
        public bool NotFound { get; set; }

        public Task<int> RunAsync(ProcessStartInfo startInfo, CancellationToken ct)
        {
            Started = startInfo;
            if (NotFound)
                throw new System.ComponentModel.Win32Exception(2, "No such file or directory");
            return Task.FromResult(ExitCode);
        }
    }

    /// <summary>
    /// Runs the CLI. <paramref name="hidden"/> answers hidden prompts (tokens, secret values);
    /// <paramref name="stdin"/>, when given, is piped to standard input.
    /// </summary>
    private async Task<(int Exit, string Out, string Error)> RunAsync(
        string[] args,
        string? hidden = null,
        string? stdin = null
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            new StringReader(stdin ?? ""),
            stdin is not null,
            name => _environment.GetValueOrDefault(name),
            _settings,
            baseUri =>
                factory.CreateClient(
                    new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                    {
                        BaseAddress = baseUri,
                    }
                ),
            _ => hidden,
            _runner
        );
        var exit = await CliApp.RunAsync(args, host);
        return (exit, output.ToString(), error.ToString());
    }

    /// <summary>An application with a secret and two configuration entries in <c>local</c>.</summary>
    private async Task<(string ApiKey, string Secret)> SeedAsync()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var secret = TestHelpers.UniqueName("api-key");
        await TestHelpers.CreateSecretWithValueAsync(Mgmt, slug, secret, "local", SecretValue);
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "Feature:DarkMode", "true");
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "Greeting", "it's $HOME");
        return (apiKey, secret);
    }

    private void UseEnvironmentCredentials(string apiKey)
    {
        _environment[CliHost.UrlVariable] = "http://localhost";
        _environment[CliHost.TokenVariable] = apiKey;
    }

    [Fact]
    public async Task Login_SavesOwnerOnlyCredentials_ThatLaterCommandsUse()
    {
        var (apiKey, secret) = await SeedAsync();

        var login = await RunAsync(["login", "--url", "http://localhost", "--app", "demo"], apiKey);

        Assert.Equal(0, login.Exit);
        var file = Path.Combine(_settings, "credentials.json");
        Assert.True(File.Exists(file));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(file)
            );

        var get = await RunAsync(["get", secret, "-e", "local"]);
        Assert.Equal(0, get.Exit);
        Assert.Equal(SecretValue, get.Out.TrimEnd());

        var logout = await RunAsync(["logout"]);
        Assert.Equal(0, logout.Exit);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Login_RejectsATokenTheApiDoesNotAccept()
    {
        var login = await RunAsync(["login", "--url", "http://localhost"], "gm_not-a-key");

        Assert.Equal(1, login.Exit);
        Assert.Contains("rejected the token", login.Error);
        Assert.False(File.Exists(Path.Combine(_settings, "credentials.json")));
    }

    [Fact]
    public async Task Commands_WithoutCredentialsOrEnvironment_ExplainWhatIsMissing()
    {
        var noCredentials = await RunAsync(["get", "x", "-e", "local"]);
        Assert.Equal(1, noCredentials.Exit);
        Assert.Contains("Not logged in", noCredentials.Error);

        UseEnvironmentCredentials("anything");
        var noEnvironment = await RunAsync(["get", "x"]);
        Assert.Equal(1, noEnvironment.Exit);
        Assert.Contains(CliApp.EnvironmentVariable, noEnvironment.Error);
    }

    [Fact]
    public async Task Get_UnknownSecret_FailsWithTheApiMessage()
    {
        var (apiKey, _) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);
        _environment[CliApp.EnvironmentVariable] = "local";

        var result = await RunAsync(["get", "does-not-exist"]);

        Assert.Equal(1, result.Exit);
        Assert.StartsWith("grimoire: ", result.Error);
        Assert.Empty(result.Out);
    }

    [Fact]
    public async Task ConfigList_PrintsSortedKeyValueLines()
    {
        var (apiKey, _) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);

        var result = await RunAsync(["config", "list", "-e", "local"]);

        Assert.Equal(0, result.Exit);
        var lines = result.Out.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(["Feature:DarkMode=true", "Greeting=it's $HOME"], lines);
    }

    [Fact]
    public async Task Export_DefaultsToEnv_AndWritesAnOwnerOnlyFile()
    {
        var (apiKey, secret) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);

        var stdout = await RunAsync(["export", "-e", "local"]);
        Assert.Equal(0, stdout.Exit);
        Assert.Contains("FEATURE__DARKMODE='true'", stdout.Out);
        Assert.Contains(
            $"{secret.ToUpperInvariant().Replace('-', '_')}='{SecretValue}'",
            stdout.Out
        );

        var path = Path.Combine(_settings, "app.json");
        var file = await RunAsync([
            "export",
            "-e",
            "local",
            "--format",
            "dotnet",
            "--include",
            "configurations",
            "-o",
            path,
        ]);
        Assert.Equal(0, file.Exit);
        Assert.Empty(file.Out);
        var json = await File.ReadAllTextAsync(path);
        Assert.Contains("\"Feature:DarkMode\"", json);
        Assert.DoesNotContain(SecretValue, json);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(path)
            );
    }

    [Fact]
    public async Task Export_RejectsUnknownFormats()
    {
        var result = await RunAsync(["export", "-e", "local", "--format", "yaml"]);

        Assert.NotEqual(0, result.Exit);
    }

    [Fact]
    public async Task Run_InjectsValuesAsEnvironmentVariables_AndReturnsTheChildExitCode()
    {
        var (apiKey, secret) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);
        _runner.ExitCode = 7;

        var result = await RunAsync(["run", "-e", "local", "--", "my-app", "--port", "8080"]);

        Assert.Equal(7, result.Exit);
        var started = _runner.Started!;
        Assert.Equal("my-app", started.FileName);
        Assert.Equal(["--port", "8080"], started.ArgumentList);
        Assert.Equal("true", started.Environment["FEATURE__DARKMODE"]);
        Assert.Equal("it's $HOME", started.Environment["GREETING"]);
        Assert.Equal(SecretValue, started.Environment[secret.ToUpperInvariant().Replace('-', '_')]);
        Assert.False(started.Environment.ContainsKey(CliHost.TokenVariable));
        Assert.DoesNotContain(SecretValue, result.Out + result.Error);
    }

    [Fact]
    public async Task Run_WithPrefix_OnlyInjectsMatchingValues()
    {
        var (apiKey, _) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);

        var result = await RunAsync(["run", "-e", "local", "--prefix", "feature:", "--", "my-app"]);

        Assert.Equal(0, result.Exit);
        var started = _runner.Started!;
        Assert.True(started.Environment.ContainsKey("FEATURE__DARKMODE"));
        Assert.False(started.Environment.ContainsKey("GREETING"));
    }

    [Fact]
    public async Task Run_ProgramNotFound_Exits127WithAMessage()
    {
        var (apiKey, _) = await SeedAsync();
        UseEnvironmentCredentials(apiKey);
        _runner.NotFound = true;

        var result = await RunAsync(["run", "-e", "local", "--", "no-such-program"]);

        Assert.Equal(127, result.Exit);
        Assert.Contains("Could not start 'no-such-program'", result.Error);
    }

    // ── Writes (set, import) ─────────────────────────────────────────────────────────────

    private void UseAdminCredentials(string slug)
    {
        _environment[CliHost.UrlVariable] = "http://localhost";
        _environment[CliHost.AdminKeyVariable] = GrimoireWebApplicationFactory.AdminKey;
        _environment[CliHost.AppVariable] = slug;
    }

    [Fact]
    public async Task LoginAdmin_SavesTheAdminKeyBesideTheToken()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);

        Assert.Equal(
            0,
            (await RunAsync(["login", "--url", "http://localhost", "--app", slug], apiKey)).Exit
        );
        var admin = await RunAsync(
            ["login", "--url", "http://localhost", "--admin"],
            GrimoireWebApplicationFactory.AdminKey
        );

        Assert.Equal(0, admin.Exit);
        var saved = new CredentialStore(Host()).Load()!;
        Assert.Equal(apiKey, saved.Token);
        Assert.Equal(GrimoireWebApplicationFactory.AdminKey, saved.AdminKey);
        Assert.Equal(slug, saved.App);

        var set = await RunAsync(["set", "Feature:Beta", "on", "-e", "local"]);
        Assert.Equal(0, set.Exit);
    }

    [Fact]
    public async Task LoginAdmin_RejectsAWrongKey_AndAnUnknownApp()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);

        var wrong = await RunAsync(
            ["login", "--url", "http://localhost", "--admin", "--app", slug],
            "not-the-admin-key"
        );
        Assert.Equal(1, wrong.Exit);
        Assert.Contains("rejected the admin key", wrong.Error);

        var unknown = await RunAsync(
            ["login", "--url", "http://localhost", "--admin", "--app", "no-such-app"],
            GrimoireWebApplicationFactory.AdminKey
        );
        Assert.Equal(1, unknown.Exit);
        Assert.False(File.Exists(Path.Combine(_settings, "credentials.json")));
    }

    [Fact]
    public async Task Set_CreatesThenUpdatesConfiguration_WithAValueType()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);

        var created = await RunAsync(["set", "Greeting", "hello", "-e", "local"]);
        var updated = await RunAsync(
            ["set", "AllowedIps", "-e", "local", "--type", "array"],
            stdin: "10.0.0.1\n10.0.0.2\n"
        );
        var again = await RunAsync(["set", "Greeting", "hi", "-e", "local"]);

        Assert.Equal(0, created.Exit);
        Assert.Contains("Created 'Greeting'", created.Out);
        Assert.Equal(0, updated.Exit);
        Assert.Contains("Updated 'Greeting'", again.Out);

        _environment[CliHost.TokenVariable] = apiKey;
        var list = await RunAsync(["config", "list", "-e", "local"]);
        Assert.Contains("Greeting=hi", list.Out);
        Assert.Contains("AllowedIps=[\"10.0.0.1\",\"10.0.0.2\"]", list.Out);
    }

    [Fact]
    public async Task Set_InvalidJson_ShowsTheApisValidationMessage()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);

        var result = await RunAsync(["set", "Smtp", "{not json", "-e", "local", "--type", "json"]);

        Assert.Equal(1, result.Exit);
        Assert.StartsWith("grimoire: ", result.Error);
        Assert.DoesNotContain("One or more validation errors", result.Error);
    }

    [Fact]
    public async Task SetSecret_ReadsTheValueFromStdin_NeverFromArguments()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);
        var name = TestHelpers.UniqueName("smtp");

        var fromArgument = await RunAsync(["set", name, SecretValue, "--secret", "-e", "local"]);
        Assert.Equal(1, fromArgument.Exit);
        Assert.Contains("shell history", fromArgument.Error);

        var piped = await RunAsync(
            ["set", name, "--secret", "-e", "local"],
            stdin: SecretValue + "\n"
        );
        Assert.Equal(0, piped.Exit);
        Assert.DoesNotContain(SecretValue, piped.Out + piped.Error);

        var prompted = await RunAsync(
            ["set", name, "--secret", "-e", "local"],
            hidden: SecretValue + "-2"
        );
        Assert.Equal(0, prompted.Exit);

        _environment[CliHost.TokenVariable] = apiKey;
        var get = await RunAsync(["get", name, "-e", "local"]);
        Assert.Equal(SecretValue + "-2", get.Out.TrimEnd());
    }

    [Fact]
    public async Task Set_WithoutAnAdminKeyOrApp_ExplainsWhatIsMissing()
    {
        _environment[CliHost.UrlVariable] = "http://localhost";
        var noKey = await RunAsync(["set", "A", "1", "-e", "local"]);
        Assert.Equal(1, noKey.Exit);
        Assert.Contains("login --admin", noKey.Error);

        _environment[CliHost.AdminKeyVariable] = GrimoireWebApplicationFactory.AdminKey;
        var noApp = await RunAsync(["set", "A", "1", "-e", "local"]);
        Assert.Equal(1, noApp.Exit);
        Assert.Contains("--app", noApp.Error);
    }

    [Fact]
    public async Task Import_ShowsThePlan_ThenAppliesWithYes()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);
        var file = Path.Combine(_settings, ".env");
        await File.WriteAllTextAsync(file, "APP_MODE=safe\nDEBUG=true\n");

        var dryRun = await RunAsync(["import", file, "-e", "local", "--dry-run"]);
        Assert.True(dryRun.Exit == 0, dryRun.Error);
        Assert.Equal(0, dryRun.Exit);
        Assert.Contains("create", dryRun.Out);
        Assert.Contains("APP_MODE", dryRun.Out);
        Assert.Contains("2 to create", dryRun.Out);

        _environment[CliHost.TokenVariable] = apiKey;
        Assert.DoesNotContain("APP_MODE", (await RunAsync(["config", "list", "-e", "local"])).Out);

        var applied = await RunAsync(["import", file, "-e", "local", "--yes"]);
        Assert.Equal(0, applied.Exit);
        Assert.Contains("2 created", applied.Out);
        Assert.Contains("APP_MODE=safe", (await RunAsync(["config", "list", "-e", "local"])).Out);

        var again = await RunAsync(["import", file, "-e", "local", "--yes"]);
        Assert.Contains("Nothing to import.", again.Out);
    }

    [Fact]
    public async Task Import_AsksBeforeApplying_AndNeedsYesWithoutATerminal()
    {
        var (slug, _) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);

        var piped = await RunAsync(
            ["import", "-", "-e", "local", "--format", "env"],
            stdin: "A=1\n"
        );
        Assert.Equal(1, piped.Exit);
        Assert.Contains("--yes", piped.Error);

        var unknown = await RunAsync([
            "import",
            Path.Combine(_settings, "settings.yaml"),
            "-e",
            "local",
        ]);
        Assert.Equal(1, unknown.Exit);
        Assert.Contains("--format", unknown.Error);
    }

    // ── Diff and value types ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Diff_ListsWhatDiffersBetweenEnvironments_WithoutSecretValues()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        var staging = await TestHelpers.CreateEnvironmentAsync(Mgmt, slug, "staging");
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, "local", "Mode", "dev");
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, staging, "Mode", "prod");
        await TestHelpers.CreateConfigurationAsync(Mgmt, slug, staging, "OnlyStaging", "x");
        var secret = TestHelpers.UniqueName("key");
        await TestHelpers.CreateSecretWithValueAsync(Mgmt, slug, secret, "local", SecretValue);
        UseEnvironmentCredentials(apiKey);

        var result = await RunAsync(["diff", "local", staging, "--exit-code"]);

        Assert.Equal(1, result.Exit);
        Assert.Contains("~ config Mode: dev → prod", result.Out);
        Assert.Contains("+ config OnlyStaging=x", result.Out);
        Assert.Contains($"- secret {secret}", result.Out);
        Assert.DoesNotContain(SecretValue, result.Out);

        var same = await RunAsync(["diff", "local", "local", "--exit-code"]);
        Assert.Equal(0, same.Exit);
        Assert.Contains("No differences.", same.Out);
    }

    [Fact]
    public async Task Export_Json_EmitsArraysAsJson_AndFlattenSplitsThemForEnvAndRun()
    {
        var (slug, apiKey) = await TestHelpers.CreateApplicationAsync(Mgmt);
        UseAdminCredentials(slug);
        Assert.Equal(
            0,
            (
                await RunAsync([
                    "set",
                    "AllowedIps",
                    "[\"10.0.0.1\",\"10.0.0.2\"]",
                    "-e",
                    "local",
                    "--type",
                    "array",
                ])
            ).Exit
        );
        _environment[CliHost.TokenVariable] = apiKey;

        var json = await RunAsync(["export", "-e", "local", "--format", "json"]);
        var document = System.Text.Json.Nodes.JsonNode.Parse(json.Out)!;
        Assert.Equal(2, document["configurations"]!["AllowedIps"]!.AsArray().Count);

        var env = await RunAsync(["export", "-e", "local", "--structured", "flatten"]);
        Assert.Contains("ALLOWEDIPS__0='10.0.0.1'", env.Out);
        Assert.DoesNotContain("ALLOWEDIPS='", env.Out);

        var raw = await RunAsync(["export", "-e", "local"]);
        Assert.Contains("ALLOWEDIPS='[\"10.0.0.1\",\"10.0.0.2\"]'", raw.Out);

        await RunAsync(["run", "-e", "local", "--structured", "both", "--", "my-app"]);
        Assert.Equal("10.0.0.2", _runner.Started!.Environment["ALLOWEDIPS__1"]);
        Assert.StartsWith("[", _runner.Started.Environment["ALLOWEDIPS"]);
    }

    private CliHost Host() =>
        new(
            TextWriter.Null,
            TextWriter.Null,
            TextReader.Null,
            false,
            name => _environment.GetValueOrDefault(name),
            _settings,
            _ => throw new InvalidOperationException(),
            _ => null,
            _runner
        );
}
