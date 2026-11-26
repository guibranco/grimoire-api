namespace Grimoire.Cli;

/// <summary>
/// Everything the CLI touches outside itself, so tests can swap the console, the
/// environment, the settings directory, the HTTP stack and process launching.
/// </summary>
/// <param name="In">Standard input, for piped values.</param>
/// <param name="InputRedirected">Whether standard input is piped rather than a terminal.</param>
/// <param name="CreateHttpClient">An HTTP client for the API base URL, without credentials.</param>
/// <param name="ReadHidden">
/// Reads one secret line: from standard input when it is piped, otherwise typed at a prompt
/// (the argument) without echoing it.
/// </param>
internal sealed record CliHost(
    TextWriter Out,
    TextWriter Error,
    TextReader In,
    bool InputRedirected,
    Func<string, string?> GetEnvironmentVariable,
    string SettingsDirectory,
    Func<Uri, HttpClient> CreateHttpClient,
    Func<string, string?> ReadHidden,
    IProcessRunner Processes
)
{
    public const string UrlVariable = "GRIMOIRE_URL";
    public const string TokenVariable = "GRIMOIRE_TOKEN";
    public const string AdminKeyVariable = "GRIMOIRE_ADMIN_KEY";
    public const string AppVariable = "GRIMOIRE_APP";
    public const string SettingsDirectoryVariable = "GRIMOIRE_CONFIG_DIR";

    public static CliHost Default()
    {
        var settings = Environment.GetEnvironmentVariable(SettingsDirectoryVariable)
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".grimoire"
            );
        return new CliHost(
            Console.Out,
            Console.Error,
            Console.In,
            Console.IsInputRedirected,
            Environment.GetEnvironmentVariable,
            settings,
            baseAddress => new HttpClient
            {
                BaseAddress = baseAddress,
                Timeout = TimeSpan.FromSeconds(30),
                DefaultRequestHeaders = { { "User-Agent", "grimoire-cli" } },
            },
            ReadHiddenFromConsole,
            new ProcessRunner()
        );
    }

    /// <summary>Everything piped to standard input, without one trailing line break.</summary>
    public string ReadAllInput()
    {
        var text = In.ReadToEnd();
        if (text.EndsWith("\r\n", StringComparison.Ordinal))
            return text[..^2];
        return text.EndsWith('\n') ? text[..^1] : text;
    }

    private static string? ReadHiddenFromConsole(string prompt)
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine()?.Trim();

        Console.Error.Write($"{prompt}: ");
        var value = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                    value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                value.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return value.ToString().Trim();
    }
}
