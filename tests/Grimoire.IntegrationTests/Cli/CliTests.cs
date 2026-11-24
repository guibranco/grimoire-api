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

    private async Task<(int Exit, string Out, string Error)> RunAsync(
        string[] args,
        string? token = null
    )
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var host = new CliHost(
            output,
            error,
            name => _environment.GetValueOrDefault(name),
            _settings,
            (baseUri, apiKey) =>
            {
                var client = factory.CreateClient(
                    new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
                    {
                        BaseAddress = baseUri,
                    }
                );
                client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);
                return client;
            },
            () => token,
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
}
