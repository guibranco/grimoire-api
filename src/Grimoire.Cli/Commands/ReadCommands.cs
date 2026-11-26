using System.CommandLine;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Grimoire.Cli.Export;
using static Grimoire.Cli.CliApp;

namespace Grimoire.Cli.Commands;

/// <summary>Commands that read through the Consumer API: <c>get</c>, <c>config list</c>, <c>export</c>, <c>run</c> and <c>diff</c>.</summary>
internal static class ReadCommands
{
    private static readonly JsonSerializerOptions Output = new()
    {
        WriteIndented = true,
        // Written to files and terminals, not HTML: keep quotes and '+' readable.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly IReadOnlyDictionary<string, string> NoTypes =
        new Dictionary<string, string>();

    public static IEnumerable<Command> Create(CliHost host) =>
        [Get(host), Config(host), Export(host), Run(host), Diff(host)];

    private static Command Get(CliHost host)
    {
        var name = new Argument<string>("name") { Description = "Secret name." };
        var environment = EnvironmentOption();
        var command = new Command("get", "Print a secret's current value.") { name, environment };
        command.SetAction(
            (parse, ct) =>
                WithConsumerAsync(host, parse.GetValue(environment), async (client, env) =>
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
            "List the effective configuration as Key=value lines (values unquoted)."
        )
        {
            environment,
        };
        list.SetAction(
            (parse, ct) =>
                WithConsumerAsync(host, parse.GetValue(environment), async (client, env) =>
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
            Description = "env (default), json (array and json values as real JSON) or dotnet.",
            DefaultValueFactory = _ => "env",
        };
        format.AcceptOnlyFromAmong([.. ExportFormatter.Formats]);
        var structured = StructuredOption();
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
            structured,
            include,
            prefix,
            output,
        };
        command.SetAction(
            (parse, ct) =>
                WithConsumerAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    var exportFormat = parse.GetValue(format)!;
                    var mode = StructuredValues.ParseMode(parse.GetValue(structured));
                    var loaded = await LoadAsync(
                        client,
                        new ExportQuery(env, parse.GetValue(include), parse.GetValue(prefix)),
                        needTypes: exportFormat == "json" || mode != StructuredMode.Raw,
                        ct
                    );
                    var body = Render(exportFormat, loaded, mode, Warn(host));
                    var file = parse.GetValue(output);
                    if (file is null)
                    {
                        host.Out.Write(body);
                        return 0;
                    }
                    await using (var writer = new StreamWriter(OwnerOnlyFile.Create(file.FullName)))
                        await writer.WriteAsync(body.AsMemory(), ct);
                    host.Error.WriteLine($"Wrote {file.FullName}.");
                    return 0;
                })
        );
        return command;
    }

    private static Command Run(CliHost host)
    {
        var environment = EnvironmentOption();
        var structured = StructuredOption();
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
            structured,
            include,
            prefix,
            commandLine,
        };
        command.SetAction(
            (parse, ct) =>
                WithConsumerAsync(host, parse.GetValue(environment), async (client, env) =>
                {
                    var mode = StructuredValues.ParseMode(parse.GetValue(structured));
                    var loaded = await LoadAsync(
                        client,
                        new ExportQuery(env, parse.GetValue(include), parse.GetValue(prefix)),
                        needTypes: mode != StructuredMode.Raw,
                        ct
                    );
                    foreach (var name in loaded.Skipped)
                        host.Error.WriteLine($"grimoire: skipped '{name}': no active version.");
                    var startInfo = ProcessRunner.Build(
                        parse.GetValue(commandLine)!,
                        ExportFormatter.ToEnvironment(Values(loaded, mode, Warn(host)))
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

    private static Command Diff(CliHost host)
    {
        var from = new Argument<string>("from") { Description = "First environment slug." };
        var to = new Argument<string>("to") { Description = "Second environment slug." };
        var (include, prefix) = FilterOptions();
        var exitCode = new Option<bool>("--exit-code")
        {
            Description = "Exit with 1 when the environments differ (for scripts and CI).",
        };
        var command = new Command(
            "diff",
            "Show what differs between two environments: configuration keys by value, secrets "
                + "by whether they have an active version. Secret values are never compared or shown."
        )
        {
            from,
            to,
            include,
            prefix,
            exitCode,
        };
        command.SetAction(
            (parse, ct) =>
            {
                if (
                    !TryParseInclude(
                        parse.GetValue(include),
                        out var secrets,
                        out var configurations
                    )
                )
                    return Task.FromResult(
                        Fail(host, "Unknown --include. Use secrets, configurations or both.")
                    );
                var fromEnv = parse.GetValue(from)!;
                var toEnv = parse.GetValue(to)!;
                var filter = parse.GetValue(prefix);
                return WithConsumerAsync(host, async client =>
                {
                    var side = new DiffSide(client, configurations, secrets, filter);
                    var a = await side.LoadAsync(fromEnv, ct);
                    var b = await side.LoadAsync(toEnv, ct);
                    var items = EnvironmentDiff.Compare(
                        a.Configurations,
                        b.Configurations,
                        a.Secrets,
                        b.Secrets
                    );
                    host.Out.WriteLine($"--- {fromEnv}");
                    host.Out.WriteLine($"+++ {toEnv}");
                    foreach (var item in items)
                        host.Out.WriteLine(EnvironmentDiff.Format(item));
                    host.Out.WriteLine(
                        items.Count == 0
                            ? "No differences."
                            : $"{items.Count} difference{(items.Count == 1 ? "" : "s")}."
                    );
                    return items.Count > 0 && parse.GetValue(exitCode) ? 1 : 0;
                });
            }
        );
        return command;
    }

    /// <summary>One environment's side of a diff.</summary>
    private sealed class DiffSide(
        GrimoireClient client,
        bool configurations,
        bool secrets,
        string? prefix
    )
    {
        public async Task<(
            IReadOnlyDictionary<string, string> Configurations,
            IReadOnlySet<string> Secrets
        )> LoadAsync(string environment, CancellationToken ct)
        {
            IReadOnlyDictionary<string, string> values = NoTypes;
            if (configurations)
                values = (
                    await client.ExportValuesAsync(
                        new ExportQuery(environment, "configurations", prefix),
                        ct
                    )
                )
                    .Values
                    .Configurations;

            var active = new HashSet<string>(StringComparer.Ordinal);
            if (secrets)
                foreach (var secret in await client.ListSecretsAsync(environment, ct))
                    if (
                        secret.Active
                        && (
                            string.IsNullOrEmpty(prefix)
                            || secret.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        )
                    )
                        active.Add(secret.Name);
            return (values, active);
        }
    }

    private sealed record Loaded(
        IReadOnlyDictionary<string, string> Secrets,
        IReadOnlyDictionary<string, string> Configurations,
        IReadOnlyDictionary<string, string> Types,
        IReadOnlyList<string> Skipped
    );

    private static async Task<Loaded> LoadAsync(
        GrimoireClient client,
        ExportQuery query,
        bool needTypes,
        CancellationToken ct
    )
    {
        var (values, skipped) = await client.ExportValuesAsync(query, ct);
        var types =
            needTypes && values.Configurations.Count > 0
                ? await client.ConfigurationTypesAsync(query.Environment, ct)
                : NoTypes;
        return new Loaded(values.Secrets, values.Configurations, types, skipped);
    }

    private static ExportedValues Values(Loaded loaded, StructuredMode mode, Action<string> warn) =>
        new(
            loaded.Secrets,
            StructuredValues.Expand(loaded.Configurations, loaded.Types, mode, warn)
        );

    private static string Render(
        string format,
        Loaded loaded,
        StructuredMode mode,
        Action<string> warn
    )
    {
        switch (format)
        {
            case "dotnet":
                return JsonSerializer.Serialize(
                        ExportFormatter.ToDotnet(Values(loaded, mode, warn)),
                        Output
                    ) + "\n";
            case "json":
                var secrets = new JsonObject();
                foreach (
                    var (name, value) in loaded.Secrets.OrderBy(s => s.Key, StringComparer.Ordinal)
                )
                    secrets[name] = value;
                var document = new JsonObject
                {
                    ["secrets"] = secrets,
                    ["configurations"] = StructuredValues.ToJsonObject(
                        loaded.Configurations,
                        loaded.Types,
                        warn
                    ),
                    ["skipped"] = new JsonArray([
                        .. loaded.Skipped.Select(s => JsonValue.Create(s)),
                    ]),
                };
                return document.ToJsonString(Output) + "\n";
            default:
                return ExportFormatter.ToEnv(Values(loaded, mode, warn));
        }
    }
}
