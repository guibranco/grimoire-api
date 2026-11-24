using System.CommandLine;
using System.Net;
using Grimoire.Cli.Export;

namespace Grimoire.Cli;

/// <summary>The <c>grimoire</c> command tree.</summary>
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
            "Command-line client for Grimoire: read secrets, export configuration and run "
                + "processes with their secrets injected."
        );
        root.Subcommands.Add(Login(host));
        root.Subcommands.Add(Logout(host));
        root.Subcommands.Add(Get(host));
        root.Subcommands.Add(Config(host));
        root.Subcommands.Add(Export(host));
        root.Subcommands.Add(Run(host));
        return root;
    }

    private static Command Login(CliHost host)
    {
        var url = new Option<string>("--url")
        {
            Description = "Grimoire API base URL, e.g. https://grimoire.example.com",
            Required = true,
        };
        var app = new Option<string?>("--app")
        {
            Description = "A label for the application the token belongs to.",
        };
        var command = new Command(
            "login",
            "Save the API URL and a token. The token is read from standard input when piped, "
                + "otherwise prompted for without echoing."
        )
        {
            url,
            app,
        };
        command.SetAction(
            async (parse, ct) =>
            {
                var baseUrl = parse.GetValue(url)!;
                if (!TryParseBaseUrl(baseUrl, out var baseUri))
                    return Fail(host, "--url must be an absolute http(s) URL.");
                var token = host.ReadToken();
                if (string.IsNullOrWhiteSpace(token))
                    return Fail(host, "No token given.");

                try
                {
                    using var http = host.CreateHttpClient(baseUri, token);
                    if (!await new GrimoireClient(http).AcceptsTokenAsync(ct))
                        return Fail(host, "Grimoire rejected the token.");
                }
                catch (HttpRequestException ex)
                {
                    return Fail(host, $"Could not reach {baseUri}: {ex.Message}");
                }

                var store = new CredentialStore(host);
                store.Save(new Credentials(baseUri.ToString(), token, parse.GetValue(app)));
                host.Out.WriteLine(
                    $"Logged in to {baseUri}. Credentials saved to {store.FilePath}."
                );
                return 0;
            }
        );
        return command;
    }

    private static Command Logout(CliHost host)
    {
        var command = new Command("logout", "Delete the saved credentials.");
        command.SetAction(_ =>
        {
            var store = new CredentialStore(host);
            host.Out.WriteLine(
                store.Delete() ? $"Deleted {store.FilePath}." : "No saved credentials."
            );
            return 0;
        });
        return command;
    }

    private static Command Get(CliHost host)
    {
        var name = new Argument<string>("name") { Description = "Secret name." };
        var environment = EnvironmentOption();
        var command = new Command("get", "Print a secret's current value.") { name, environment };
        command.SetAction(
            (parse, ct) => WithClientAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    host.Out.WriteLine(await client.GetSecretAsync(parse.GetValue(name)!, env, ct));
                    return 0;
                })
        );
        return command;
    }

    private static Command Config(CliHost host)
    {
        var environment = EnvironmentOption();
        var list = new Command(
            "list",
            "List the effective configuration as KEY=value lines (values unquoted)."
        )
        {
            environment,
        };
        list.SetAction(
            (parse, ct) => WithClientAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    var items = await client.ListConfigurationAsync(env, ct);
                    foreach (var item in items.OrderBy(i => i.Key, StringComparer.Ordinal))
                        host.Out.WriteLine($"{item.Key}={item.Value}");
                    return 0;
                })
        );
        return new Command("config", "Read configuration.") { list };
    }

    private static Command Export(CliHost host)
    {
        var environment = EnvironmentOption();
        var format = new Option<string>("--format", "-f")
        {
            Description = "env (default), json or dotnet.",
            DefaultValueFactory = _ => "env",
        };
        format.AcceptOnlyFromAmong([.. ExportFormatter.Formats]);
        var (include, prefix) = FilterOptions();
        var output = new Option<FileInfo?>("--output", "-o")
        {
            Description = "Write to this file (readable only by you) instead of standard output.",
        };
        var command = new Command(
            "export",
            "Export every value the application can read in an environment."
        )
        {
            environment,
            format,
            include,
            prefix,
            output,
        };
        command.SetAction(
            (parse, ct) => WithClientAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    var query = new ExportQuery(
                        env,
                        parse.GetValue(include),
                        parse.GetValue(prefix)
                    );
                    var body = await client.ExportAsync(query, parse.GetValue(format)!, ct);
                    var file = parse.GetValue(output);
                    if (file is null)
                    {
                        host.Out.Write(body);
                        return 0;
                    }
                    await WriteOwnerOnlyAsync(file.FullName, body, ct);
                    host.Error.WriteLine($"Wrote {file.FullName}.");
                    return 0;
                })
        );
        return command;
    }

    private static Command Run(CliHost host)
    {
        var environment = EnvironmentOption();
        var (include, prefix) = FilterOptions();
        var commandLine = new Argument<string[]>("command")
        {
            Description = "The program and its arguments, after --.",
            Arity = ArgumentArity.OneOrMore,
        };
        var command = new Command(
            "run",
            "Run a program with the environment's secrets and configuration as environment "
                + "variables (Feature:DarkMode → FEATURE__DARKMODE). Nothing is written to disk."
        )
        {
            environment,
            include,
            prefix,
            commandLine,
        };
        command.SetAction(
            (parse, ct) => WithClientAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    var query = new ExportQuery(
                        env,
                        parse.GetValue(include),
                        parse.GetValue(prefix)
                    );
                    var (values, skipped) = await client.ExportValuesAsync(query, ct);
                    foreach (var name in skipped)
                        host.Error.WriteLine($"grimoire: skipped '{name}': no active version.");
                    var startInfo = ProcessRunner.Build(
                        parse.GetValue(commandLine)!,
                        ExportFormatter.ToEnvironment(values)
                    );
                    try
                    {
                        return await host.Processes.RunAsync(startInfo, ct);
                    }
                    catch (System.ComponentModel.Win32Exception ex)
                    {
                        // Shells use 127 for "command not found".
                        Fail(host, $"Could not start '{startInfo.FileName}': {ex.Message}");
                        return 127;
                    }
                })
        );
        return command;
    }

    private static Option<string?> EnvironmentOption() =>
        new("--environment", "-e")
        {
            Description = $"Environment slug. Defaults to ${EnvironmentVariable}.",
        };

    private static (Option<string?> Include, Option<string?> Prefix) FilterOptions() =>
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

    /// <summary>
    /// Resolves the environment and credentials, then runs <paramref name="action"/>, turning
    /// API and network failures into a message on standard error and exit code 1.
    /// </summary>
    private static async Task<int> WithClientAsync(
        CliHost host,
        string? environment,
        Func<GrimoireClient, string, Task<int>> action
    )
    {
        environment ??= host.GetEnvironmentVariable(EnvironmentVariable);
        if (string.IsNullOrWhiteSpace(environment))
            return Fail(host, $"Pass --environment or set {EnvironmentVariable}.");

        var credentials = new CredentialStore(host).Resolve();
        if (credentials is null)
            return Fail(
                host,
                $"Not logged in. Run 'grimoire login' or set {CliHost.UrlVariable} and {CliHost.TokenVariable}."
            );
        if (!TryParseBaseUrl(credentials.Url, out var baseUri))
            return Fail(host, $"'{credentials.Url}' is not an absolute http(s) URL.");

        try
        {
            using var http = host.CreateHttpClient(baseUri, credentials.Token);
            return await action(new GrimoireClient(http), environment);
        }
        catch (GrimoireApiException ex)
        {
            var hint =
                ex.Status == HttpStatusCode.Unauthorized ? " Run 'grimoire login' again." : "";
            return Fail(host, ex.Message + hint);
        }
        catch (HttpRequestException ex)
        {
            return Fail(host, $"Could not reach {baseUri}: {ex.Message}");
        }
    }

    private static bool TryParseBaseUrl(string value, out Uri uri)
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

    private static int Fail(CliHost host, string message)
    {
        host.Error.WriteLine($"grimoire: {message}");
        return 1;
    }

    private static async Task WriteOwnerOnlyAsync(string path, string content, CancellationToken ct)
    {
        await using var writer = new StreamWriter(OwnerOnlyFile.Create(path));
        await writer.WriteAsync(content.AsMemory(), ct);
    }
}
