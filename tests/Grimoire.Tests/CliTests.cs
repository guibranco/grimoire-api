using Grimoire.Cli;

namespace Grimoire.Tests;

public sealed class CliTests : IDisposable
{
    private readonly string _settings = Directory.CreateTempSubdirectory("grimoire-cli-").FullName;
    private readonly Dictionary<string, string> _environment = [];

    public void Dispose() => Directory.Delete(_settings, recursive: true);

    private CliHost Host() =>
        new(
            TextWriter.Null,
            TextWriter.Null,
            name => _environment.GetValueOrDefault(name),
            _settings,
            (_, _) => throw new InvalidOperationException("No HTTP in unit tests."),
            () => null,
            new ProcessRunner()
        );

    [Fact]
    public void Credentials_EnvironmentVariablesOverrideTheSavedFile()
    {
        var store = new CredentialStore(Host());
        store.Save(new Credentials("https://saved.example.com/", "saved-token", "demo"));

        Assert.Equal("saved-token", store.Resolve()!.Token);

        _environment[CliHost.UrlVariable] = "https://ci.example.com";
        _environment[CliHost.TokenVariable] = "ci-token";
        var resolved = store.Resolve()!;
        Assert.Equal("https://ci.example.com", resolved.Url);
        Assert.Equal("ci-token", resolved.Token);
        Assert.Equal("demo", resolved.App);
    }

    [Fact]
    public void Credentials_AreNullWithoutAUrlAndAToken()
    {
        var store = new CredentialStore(Host());
        Assert.Null(store.Resolve());

        _environment[CliHost.TokenVariable] = "only-a-token";
        Assert.Null(store.Resolve());
    }

    [Fact]
    public void Credentials_IgnoreACorruptFile()
    {
        File.WriteAllText(Path.Combine(_settings, "credentials.json"), "{ not json");

        Assert.Null(new CredentialStore(Host()).Resolve());
    }

    [Fact]
    public void Build_PassesArgumentsVerbatim_AddsVariables_AndDropsTheCliToken()
    {
        System.Diagnostics.ProcessStartInfo startInfo;
        Environment.SetEnvironmentVariable(CliHost.TokenVariable, "inherited");
        try
        {
            startInfo = ProcessRunner.Build(
                ["app", "--name", "two words"],
                new Dictionary<string, string> { ["FEATURE__DARKMODE"] = "true" }
            );
        }
        finally
        {
            Environment.SetEnvironmentVariable(CliHost.TokenVariable, null);
        }

        Assert.Equal("app", startInfo.FileName);
        Assert.Equal(["--name", "two words"], startInfo.ArgumentList);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal("true", startInfo.Environment["FEATURE__DARKMODE"]);
        Assert.False(startInfo.Environment.ContainsKey(CliHost.TokenVariable));
    }

    [Fact]
    public async Task Run_TheChildSeesTheVariables_AndItsExitCodeComesBack()
    {
        if (OperatingSystem.IsWindows())
            return;

        var value = "it's $HOME \"quoted\"";
        var check = ProcessRunner.Build(
            ["sh", "-c", "test \"$GREETING\" = \"$1\" && exit 3", "sh", value],
            new Dictionary<string, string> { ["GREETING"] = value }
        );

        Assert.Equal(3, await new ProcessRunner().RunAsync(check, CancellationToken.None));
    }

    [Fact]
    public async Task Run_UnknownProgram_Throws()
    {
        var startInfo = ProcessRunner.Build(
            [$"grimoire-no-such-program-{Guid.NewGuid():N}"],
            new Dictionary<string, string>()
        );

        await Assert.ThrowsAnyAsync<Exception>(() =>
            new ProcessRunner().RunAsync(startInfo, CancellationToken.None)
        );
    }
}
