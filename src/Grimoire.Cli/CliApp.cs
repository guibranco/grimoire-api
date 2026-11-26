using System.CommandLine;
using System.Net;
using Grimoire.Cli.Commands;

namespace Grimoire.Cli;

/// <summary>The <c>grimoire</c> command tree and the plumbing its commands share.</summary>
internal static class CliApp
{
    public const string EnvironmentVariable = "GRIMOIRE_ENVIRONMENT";

    public static Task<int> RunAsync(string[] args, CliHost host, CancellationToken ct = default)
    {
        var root = Build(host);
        var invocation = new InvocationConfiguration
        {
            Output = host.Out,
            Error = host.Error,
            // `run` lets Ctrl+C reach the child and waits for it; don't cancel underneath it.
            ProcessTerminationTimeout = null,
        };
        return root.Parse(args).InvokeAsync(invocation, ct);
    }

    internal static RootCommand Build(CliHost host)
    {
        var root = new RootCommand(
            "Command-line client for Grimoire: read and write secrets and configuration, "
                + "export them, compare environments and run processes with their secrets injected."
        );
        foreach (var command in AccountCommands.Create(host))
            root.Subcommands.Add(command);
        foreach (var command in ReadCommands.Create(host))
            root.Subcommands.Add(command);
        foreach (var command in WriteCommands.Create(host))
            root.Subcommands.Add(command);
        return root;
    }

    internal static Option<string?> EnvironmentOption() =>
        new("--environment", "-e")
        {
            Description = $"Environment slug. Defaults to ${EnvironmentVariable}.",
        };

    internal static Option<string?> AppOption() =>
        new("--app")
        {
            Description = $"Application slug. Defaults to the saved one or ${CliHost.AppVariable}.",
        };

    internal static (Option<string?> Include, Option<string?> Prefix) FilterOptions() =>
        (
            new Option<string?>("--include")
            {
                Description = "secrets, configurations or both, comma-separated (default both).",
            },
            new Option<string?>("--prefix")
            {
                Description = "Only keys and secret names starting with this, ignoring case.",
            }
        );

    internal static Option<string> StructuredOption()
    {
        var option = new Option<string>("--structured")
        {
            Description =
                "How array and json values are written: raw (default, the JSON text), "
                + "flatten (one entry per item, AllowedIps:0 → ALLOWEDIPS__0) or both.",
            DefaultValueFactory = _ => "raw",
        };
        option.AcceptOnlyFromAmong(StructuredValues.Modes);
        return option;
    }

    /// <summary>
    /// Whether an <c>--include</c> list selects secrets and configurations (both when empty).
    /// False for anything else, which the API would reject too.
    /// </summary>
    internal static bool TryParseInclude(string? include, out bool secrets, out bool configurations)
    {
        secrets = configurations = string.IsNullOrWhiteSpace(include);
        if (secrets)
            return true;
        foreach (
            var part in include!.Split(
                ',',
                StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
            )
        )
        {
            if (part.Equals("secrets", StringComparison.OrdinalIgnoreCase))
                secrets = true;
            else if (part.Equals("configurations", StringComparison.OrdinalIgnoreCase))
                configurations = true;
            else
                return false;
        }
        return secrets || configurations;
    }

    /// <summary>
    /// Runs a read command: resolves the environment and the read token, then turns API and
    /// network failures into a message on standard error and exit code 1.
    /// </summary>
    internal static Task<int> WithConsumerAsync(
        CliHost host,
        string? environment,
        Func<GrimoireClient, string, Task<int>> action
    )
    {
        environment ??= host.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(environment))
            return Task.FromResult(Fail(host, $"Pass --environment or set {EnvironmentVariable}."));
        return WithConsumerAsync(host, client => action(client, environment));
    }

    internal static Task<int> WithConsumerAsync(
        CliHost host,
        Func<GrimoireClient, Task<int>> action
    )
    {
        var credentials = new CredentialStore(host).Resolve();
        if (credentials?.Token is not { Length: > 0 } token)
            return Task.FromResult(
                Fail(
                    host,
                    $"Not logged in. Run 'grimoire login' or set {CliHost.UrlVariable} and {CliHost.TokenVariable}."
                )
            );
        return GuardedAsync(host, credentials.Url, "grimoire login", http =>
            action(new GrimoireClient(http, token))
        );
    }

    /// <summary>
    /// Runs a write command: resolves the environment, the application and the admin key, then
    /// turns API and network failures into a message on standard error and exit code 1.
    /// </summary>
    internal static Task<int> WithManagementAsync(
        CliHost host,
        string? environment,
        string? app,
        Func<ManagementClient, string, Task<int>> action
    )
    {
        environment ??= host.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(environment))
            return Task.FromResult(Fail(host, $"Pass --environment or set {EnvironmentVariable}."));

        var credentials = new CredentialStore(host).Resolve();
        if (credentials?.AdminKey is not { Length: > 0 } adminKey)
            return Task.FromResult(
                Fail(
                    host,
                    $"No admin key. Run 'grimoire login --admin' or set {CliHost.UrlVariable} and {CliHost.AdminKeyVariable}."
                )
            );
        app ??= credentials.App;
        if (string.IsNullOrWhiteSpace(app))
            return Task.FromResult(
                Fail(
                    host,
                    $"Pass --app, save one with 'grimoire login --app' or set {CliHost.AppVariable}."
                )
            );
        return GuardedAsync(host, credentials.Url, "grimoire login --admin", http =>
            action(new ManagementClient(http, adminKey, app), environment)
        );
    }

    /// <summary>Creates the HTTP client for <paramref name="url"/> and maps failures to exit code 1.</summary>
    internal static async Task<int> GuardedAsync(
        CliHost host,
        string url,
        string loginHint,
        Func<HttpClient, Task<int>> action
    )
    {
        if (!TryParseBaseUrl(url, out var baseUri))
            return Fail(host, $"'{url}' is not an absolute http(s) URL.");
        try
        {
            using var http = host.CreateHttpClient(baseUri);
            return await action(http);
        }
        catch (GrimoireApiException ex)
        {
            var hint = ex.Status == HttpStatusCode.Unauthorized ? $" Run '{loginHint}' again." : "";
            return Fail(host, ex.Message + hint);
        }
        catch (HttpRequestException ex)
        {
            return Fail(host, $"Could not reach {baseUri}: {ex.Message}");
        }
    }

    internal static bool TryParseBaseUrl(string value, out Uri uri)
    {
        var withSlash = value.EndsWith('/') ? value : value + "/";
        if (
            Uri.TryCreate(withSlash, UriKind.Absolute, out uri!)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
        )
            return true;
        uri = null!;
        return false;
    }

    internal static int Fail(CliHost host, string message)
    {
        host.Error.WriteLine($"grimoire: {message}");
        return 1;
    }

    internal static Action<string> Warn(CliHost host) =>
        message => host.Error.WriteLine($"grimoire: {message}");
}
