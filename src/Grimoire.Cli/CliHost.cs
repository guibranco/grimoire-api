namespace Grimoire.Cli;

/// <summary>
/// Everything the CLI touches outside itself, so tests can swap the console, the
/// environment, the settings directory, the HTTP stack and process launching.
/// </summary>
internal sealed record CliHost(
    TextWriter Out,
    TextWriter Error,
    Func<string, string?> GetEnvironmentVariable,
    string SettingsDirectory,
    Func<Uri, string, HttpClient> CreateHttpClient,
    Func<string?> ReadToken,
    IProcessRunner Processes
)
{
    public const string UrlVariable = "GRIMOIRE_URL";
    public const string TokenVariable = "GRIMOIRE_TOKEN";
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
            Environment.GetEnvironmentVariable,
            settings,
            GrimoireClient.CreateHttpClient,
            ReadTokenFromConsole,
            new ProcessRunner()
        );
    }

    /// <summary>
    /// The token from standard input when it is piped (<c>echo $TOKEN | grimoire login …</c>),
    /// otherwise typed at a prompt without echoing it.
    /// </summary>
    private static string? ReadTokenFromConsole()
    {
        if (Console.IsInputRedirected)
            return Console.In.ReadLine()?.Trim();

        Console.Error.Write("Token: ");
        var token = new System.Text.StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
                break;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (token.Length > 0)
                    token.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar))
                token.Append(key.KeyChar);
        }
        Console.Error.WriteLine();
        return token.ToString().Trim();
    }
}
