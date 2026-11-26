using System.Text.Json;

namespace Grimoire.Cli;

/// <summary>Where to reach Grimoire and what to send.</summary>
/// <param name="Url">API base URL.</param>
/// <param name="Token">An application API key or access token (sent as <c>X-Api-Key</c>), for reads.</param>
/// <param name="App">The application slug, for writes; optional for reads.</param>
/// <param name="AdminKey">The management admin key, for writes (<c>set</c>, <c>import</c>).</param>
internal sealed record Credentials(
    string Url,
    string? Token,
    string? App = null,
    string? AdminKey = null
);

/// <summary>
/// Saved credentials in <c>~/.grimoire/credentials.json</c> (readable only by the owner on
/// Unix). <c>GRIMOIRE_URL</c>, <c>GRIMOIRE_TOKEN</c>, <c>GRIMOIRE_APP</c> and
/// <c>GRIMOIRE_ADMIN_KEY</c> override what is saved, for CI.
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

        // Create the file owner-only before anything secret is written to it.
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

    /// <summary>The saved credentials only, without environment overrides.</summary>
    public Credentials? Load()
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

    /// <summary>The credentials to use, or <c>null</c> when no URL is known.</summary>
    public Credentials? Resolve()
    {
        var saved = Load();
        var url = Override(CliHost.UrlVariable) ?? saved?.Url;
        if (string.IsNullOrWhiteSpace(url))
            return null;
        return new Credentials(
            url,
            Override(CliHost.TokenVariable) ?? saved?.Token,
            Override(CliHost.AppVariable) ?? saved?.App,
            Override(CliHost.AdminKeyVariable) ?? saved?.AdminKey
        );
    }

    private string? Override(string name) =>
        host.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
}
