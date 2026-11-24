using System.Text.Json;

namespace Grimoire.Cli;

/// <summary>Where to reach Grimoire and the token to send.</summary>
/// <param name="Url">API base URL.</param>
/// <param name="Token">An application API key or access token (sent as <c>X-Api-Key</c>).</param>
/// <param name="App">Optional label for the application the token belongs to.</param>
internal sealed record Credentials(string Url, string Token, string? App = null);

/// <summary>
/// Saved credentials in <c>~/.grimoire/credentials.json</c> (readable only by the owner on
/// Unix). <c>GRIMOIRE_URL</c> and <c>GRIMOIRE_TOKEN</c> override what is saved, for CI.
/// </summary>
internal sealed class CredentialStore(CliHost host)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public string FilePath => Path.Combine(host.SettingsDirectory, "credentials.json");

    public void Save(Credentials credentials)
    {
        Directory.CreateDirectory(host.SettingsDirectory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(
                host.SettingsDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );

        // Create the file owner-only before the token is written to it.
        using (var stream = OwnerOnlyFile.Create(FilePath))
            JsonSerializer.Serialize(stream, credentials, Json);
    }

    public bool Delete()
    {
        if (!File.Exists(FilePath))
            return false;
        File.Delete(FilePath);
        return true;
    }

    /// <summary>The credentials to use, or <c>null</c> when there are none.</summary>
    public Credentials? Resolve()
    {
        var saved = Load();
        var url = host.GetEnvironmentVariable(CliHost.UrlVariable) ?? saved?.Url;
        var token = host.GetEnvironmentVariable(CliHost.TokenVariable) ?? saved?.Token;
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(token))
            return null;
        return new Credentials(url, token, saved?.App);
    }

    private Credentials? Load()
    {
        if (!File.Exists(FilePath))
            return null;
        try
        {
            using var stream = File.OpenRead(FilePath);
            return JsonSerializer.Deserialize<Credentials>(stream, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
