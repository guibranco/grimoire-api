using System.CommandLine;
using static Grimoire.Cli.CliApp;

namespace Grimoire.Cli.Commands;

/// <summary><c>login</c> and <c>logout</c>.</summary>
internal static class AccountCommands
{
    public static IEnumerable<Command> Create(CliHost host) => [Login(host), Logout(host)];

    private static Command Login(CliHost host)
    {
        var url = new Option<string>("--url")
        {
            Description = "Grimoire API base URL, e.g. https://grimoire.example.com",
            Required = true,
        };
        var app = new Option<string?>("--app")
        {
            Description =
                "The application's slug. Required with --admin; saved for set and import.",
        };
        var admin = new Option<bool>("--admin")
        {
            Description =
                "Save the management admin key (for set and import) instead of a read token.",
        };
        var command = new Command(
            "login",
            "Save the API URL and a read token (or, with --admin, the admin key). The secret is "
                + "read from standard input when piped, otherwise prompted for without echoing. "
                + "Run it once for each to save both."
        )
        {
            url,
            app,
            admin,
        };
        command.SetAction(
            (parse, ct) =>
            {
                if (!TryParseBaseUrl(parse.GetValue(url)!, out var baseUri))
                    return Task.FromResult(Fail(host, "--url must be an absolute http(s) URL."));
                return parse.GetValue(admin)
                    ? LoginAdminAsync(host, baseUri, parse.GetValue(app), ct)
                    : LoginTokenAsync(host, baseUri, parse.GetValue(app), ct);
            }
        );
        return command;
    }

    private static Task<int> LoginTokenAsync(
        CliHost host,
        Uri baseUri,
        string? app,
        CancellationToken ct
    )
    {
        var token = host.ReadHidden("Token");
        if (string.IsNullOrWhiteSpace(token))
            return Task.FromResult(Fail(host, "No token given."));

        return GuardedAsync(host, baseUri.ToString(), "grimoire login", async http =>
        {
            if (!await new GrimoireClient(http, token).AcceptsTokenAsync(ct))
                return Fail(host, "Grimoire rejected the token.");
            var saved = SavedFor(host, baseUri);
            return Save(
                host,
                new Credentials(baseUri.ToString(), token, app ?? saved?.App, saved?.AdminKey),
                $"Logged in to {baseUri}"
            );
        });
    }

    private static Task<int> LoginAdminAsync(
        CliHost host,
        Uri baseUri,
        string? app,
        CancellationToken ct
    )
    {
        var saved = SavedFor(host, baseUri);
        app ??= saved?.App;
        if (string.IsNullOrWhiteSpace(app))
            return Task.FromResult(Fail(host, "Pass --app with --admin."));
        var adminKey = host.ReadHidden("Admin key");
        if (string.IsNullOrWhiteSpace(adminKey))
            return Task.FromResult(Fail(host, "No admin key given."));

        return GuardedAsync(host, baseUri.ToString(), "grimoire login --admin", async http =>
        {
            if (!await new ManagementClient(http, adminKey, app).AcceptsKeyAsync(ct))
                return Fail(host, "Grimoire rejected the admin key.");
            return Save(
                host,
                new Credentials(baseUri.ToString(), saved?.Token, app, adminKey),
                $"Saved the admin key for {app} on {baseUri}"
            );
        });
    }

    /// <summary>The saved credentials when they are for the same server, so the other secret is kept.</summary>
    private static Credentials? SavedFor(CliHost host, Uri baseUri)
    {
        var saved = new CredentialStore(host).Load();
        return saved?.Url == baseUri.ToString() ? saved : null;
    }

    private static int Save(CliHost host, Credentials credentials, string message)
    {
        var store = new CredentialStore(host);
        store.Save(credentials);
        host.Out.WriteLine($"{message}. Credentials saved to {store.FilePath}.");
        return 0;
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
}
