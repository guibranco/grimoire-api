using System.CommandLine;
using static Grimoire.Cli.CliApp;

namespace Grimoire.Cli.Commands;

/// <summary>Commands that write through the Management API with the admin key: <c>set</c> and <c>import</c>.</summary>
internal static class WriteCommands
{
    private static readonly string[] ValueTypes = ["string", "multiline", "array", "json"];
    private static readonly string[] ImportFormats = ["env", "appsettings", "json", "php"];

    public static IEnumerable<Command> Create(CliHost host) => [Set(host), Import(host)];

    private static Command Set(CliHost host)
    {
        var key = new Argument<string>("key") { Description = "Configuration key or secret name." };
        var value = new Argument<string?>("value")
        {
            Description =
                "The value (configuration only). Leave out to read it from standard input.",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var environment = EnvironmentOption();
        var app = AppOption();
        var secret = new Option<bool>("--secret")
        {
            Description =
                "Write a secret. Its value is read from standard input or a hidden prompt, never "
                + "from the command line, so it stays out of your shell history.",
        };
        var type = new Option<string?>("--type")
        {
            Description =
                "Configuration value type: string, multiline, array (JSON or one item per line) "
                + "or json. Leave out to keep the entry's type (string for a new one).",
        };
        type.AcceptOnlyFromAmong(ValueTypes);
        var command = new Command(
            "set",
            "Create or update a configuration entry, or write a new version of a secret."
        )
        {
            key,
            value,
            environment,
            app,
            secret,
            type,
        };
        command.SetAction(
            (parse, ct) =>
            {
                var name = parse.GetValue(key)!;
                var isSecret = parse.GetValue(secret);
                var valueType = parse.GetValue(type);
                if (isSecret && valueType is not null)
                    return Task.FromResult(Fail(host, "--type only applies to configuration."));
                if (isSecret && parse.GetValue(value) is not null)
                    return Task.FromResult(
                        Fail(
                            host,
                            "Secret values aren't taken as arguments (they'd end up in your shell "
                                + "history). Pipe the value in or type it at the prompt."
                        )
                    );

                var content = ReadValue(host, name, isSecret, parse.GetValue(value));
                if (string.IsNullOrEmpty(content))
                    return Task.FromResult(
                        Fail(host, "No value given. Pass one, or pipe it to standard input.")
                    );

                return WithManagementAsync(
                    host,
                    parse.GetValue(environment),
                    parse.GetValue(app),
                    async (client, env) =>
                    {
                        if (isSecret)
                        {
                            await client.SetSecretAsync(env, name, content, ct);
                            host.Out.WriteLine($"Saved a new version of secret '{name}' in {env}.");
                            return 0;
                        }
                        var created = await client.SetConfigurationAsync(
                            env,
                            name,
                            content,
                            valueType,
                            ct
                        );
                        host.Out.WriteLine(
                            $"{(created ? "Created" : "Updated")} '{name}' in {env}."
                        );
                        return 0;
                    }
                );
            }
        );
        return command;
    }

    /// <summary>The argument, else everything piped in, else (for secrets) a hidden prompt.</summary>
    private static string? ReadValue(CliHost host, string name, bool isSecret, string? argument)
    {
        if (argument is not null)
            return argument;
        if (host.InputRedirected)
            return host.ReadAllInput();
        return isSecret ? host.ReadHidden($"Value for {name}") : null;
    }

    private static Command Import(CliHost host)
    {
        var file = new Argument<string>("file")
        {
            Description = "The file to import, or - for standard input.",
        };
        var environment = EnvironmentOption();
        var app = AppOption();
        var format = new Option<string?>("--format", "-f")
        {
            Description =
                "env, appsettings, json or php. Detected from the file name when left out.",
        };
        format.AcceptOnlyFromAmong(ImportFormats);
        var secretKeys = new Option<string?>("--secret-keys")
        {
            Description =
                "Comma-separated name patterns to import as secrets (e.g. *PASSWORD*,*_KEY). "
                + "Defaults to the server's list.",
        };
        var exclude = new Option<string?>("--exclude")
        {
            Description = "Comma-separated keys to leave out.",
        };
        var overwrite = new Option<bool>("--overwrite")
        {
            Description = "Replace values that already exist (default: keep them).",
        };
        var dryRun = new Option<bool>("--dry-run")
        {
            Description = "Show the plan without changing anything.",
        };
        var yes = new Option<bool>("--yes", "-y")
        {
            Description = "Apply without asking. Required when standard input isn't a terminal.",
        };
        var command = new Command(
            "import",
            "Import a .env, appsettings.json, JSON or PHP config file. Shows the plan first, then "
                + "applies it once you confirm."
        )
        {
            file,
            environment,
            app,
            format,
            secretKeys,
            exclude,
            overwrite,
            dryRun,
            yes,
        };
        command.SetAction(
            (parse, ct) =>
            {
                var path = parse.GetValue(file)!;
                var importFormat = parse.GetValue(format) ?? DetectFormat(path);
                var apply = !parse.GetValue(dryRun);
                var confirmed = parse.GetValue(yes);
                if (CheckImport(host, path, importFormat, apply, confirmed) is { } problem)
                    return Task.FromResult(Fail(host, problem));

                var options = new ImportOptions(
                    importFormat!,
                    path == "-" ? host.ReadAllInput() : File.ReadAllText(path),
                    SplitList(parse.GetValue(secretKeys)),
                    parse.GetValue(overwrite),
                    SplitList(parse.GetValue(exclude))
                );
                return WithManagementAsync(
                    host,
                    parse.GetValue(environment),
                    parse.GetValue(app),
                    (client, env) => ImportAsync(host, client, env, options, apply, confirmed, ct)
                );
            }
        );
        return command;
    }

    /// <summary>Why the import can't start, or <c>null</c> when it can.</summary>
    private static string? CheckImport(
        CliHost host,
        string path,
        string? format,
        bool apply,
        bool confirmed
    )
    {
        if (format is null)
            return "Can't tell the file's format from its name. Pass --format.";
        var fromStdin = path == "-";
        if (!fromStdin && !File.Exists(path))
            return $"File not found: {path}";
        if (apply && !confirmed && (fromStdin || host.InputRedirected))
            return "Standard input isn't a terminal to confirm on. Pass --yes or --dry-run.";
        return null;
    }

    /// <summary>Shows the dry-run plan, then applies it when asked to and confirmed.</summary>
    private static async Task<int> ImportAsync(
        CliHost host,
        ManagementClient client,
        string env,
        ImportOptions options,
        bool apply,
        bool confirmed,
        CancellationToken ct
    )
    {
        var plan = await client.ImportAsync(env, options, dryRun: true, ct);
        PrintPlan(host, plan);
        if (!apply)
            return 0;
        if (plan.Summary.Create + plan.Summary.Update == 0)
        {
            host.Out.WriteLine("Nothing to import.");
            return 0;
        }
        if (!confirmed && !Confirm(host, $"Apply to {env}?"))
        {
            host.Out.WriteLine("Nothing changed.");
            return 0;
        }
        var applied = await client.ImportAsync(env, options, dryRun: false, ct);
        host.Out.WriteLine(
            $"Imported into {env}: {applied.Summary.Create} created, {applied.Summary.Update} updated."
        );
        return 0;
    }

    /// <summary>The import format a file name implies, or <c>null</c>.</summary>
    internal static string? DetectFormat(string path)
    {
        var name = Path.GetFileName(path).ToLowerInvariant();
        if (
            name == ".env"
            || name.StartsWith(".env.", StringComparison.Ordinal)
            || name.EndsWith(".env", StringComparison.Ordinal)
        )
            return "env";
        if (
            name.StartsWith("appsettings", StringComparison.Ordinal)
            && name.EndsWith(".json", StringComparison.Ordinal)
        )
            return "appsettings";
        if (name.EndsWith(".json", StringComparison.Ordinal))
            return "json";
        return name.EndsWith(".php", StringComparison.Ordinal) ? "php" : null;
    }

    private static List<string>? SplitList(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            :
            [
                .. value.Split(
                    ',',
                    StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries
                ),
            ];

    private static void PrintPlan(CliHost host, ImportPlan plan)
    {
        foreach (var item in plan.Items.Where(i => i.Action != "unchanged"))
            host.Out.WriteLine($"  {item.Action, -9} {item.Kind, -13} {item.Key}");
        foreach (var skipped in plan.Skipped)
            host.Out.WriteLine($"  {"ignored", -9} {"", -13} {skipped}");
        var s = plan.Summary;
        host.Out.WriteLine(
            $"{s.Create} to create, {s.Update} to update, {s.Unchanged} unchanged, {s.Skipped} skipped."
        );
    }

    private static bool Confirm(CliHost host, string question)
    {
        host.Error.Write($"{question} [y/N] ");
        var answer = host.In.ReadLine()?.Trim();
        return answer is not null
            && (
                answer.Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Equals("yes", StringComparison.OrdinalIgnoreCase)
            );
    }
}
