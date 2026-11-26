using System.Net;

namespace Grimoire.Cli;

/// <summary>The Management API calls the CLI makes, authenticated with the admin key.</summary>
internal sealed class ManagementClient : ApiClient
{
    private readonly string _app;

    public ManagementClient(HttpClient http, string adminKey, string app)
        : base(http)
    {
        _app = Escape(app);
        http.DefaultRequestHeaders.Remove("Authorization");
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {adminKey}");
    }

    private string AppUrl => $"management/applications/{_app}";

    /// <summary>
    /// Whether the API accepts the admin key and knows the application. Throws for any other
    /// failure; returns false only for <c>401</c>.
    /// </summary>
    public async Task<bool> AcceptsKeyAsync(CancellationToken ct)
    {
        using var response = await Http.GetAsync(AppUrl, ct);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return false;
        await EnsureSuccessAsync(response, ct);
        return true;
    }

    /// <summary>Creates the configuration entry, or updates it when it exists. Returns whether it was created.</summary>
    public async Task<bool> SetConfigurationAsync(
        string environment,
        string key,
        string value,
        string? valueType,
        CancellationToken ct
    )
    {
        using (
            var created = await SendAsync(
                HttpMethod.Post,
                $"{AppUrl}/configurations",
                new
                {
                    environmentSlug = environment,
                    key,
                    value,
                    valueType,
                },
                ct
            )
        )
        {
            if (created.StatusCode != HttpStatusCode.Conflict)
            {
                await EnsureSuccessAsync(created, ct);
                return true;
            }
        }

        using var updated = await SendAsync(
            HttpMethod.Put,
            $"{AppUrl}/configurations/{Escape(environment)}/{Escape(key)}",
            new { value, valueType },
            ct
        );
        await EnsureSuccessAsync(updated, ct);
        return false;
    }

    /// <summary>Creates the secret if needed, then writes a new version of its value in the environment.</summary>
    public async Task SetSecretAsync(
        string environment,
        string name,
        string value,
        CancellationToken ct
    )
    {
        using (
            var created = await SendAsync(
                HttpMethod.Post,
                $"{AppUrl}/secrets",
                new { name, description = (string?)null },
                ct
            )
        )
        {
            if (created.StatusCode != HttpStatusCode.Conflict)
                await EnsureSuccessAsync(created, ct);
        }

        using var written = await SendAsync(
            HttpMethod.Post,
            $"{AppUrl}/secrets/{Escape(name)}/values",
            new[] { new { environmentSlug = environment, value } },
            ct
        );
        await EnsureSuccessAsync(written, ct);
    }

    public Task<ImportPlan> ImportAsync(
        string environment,
        ImportOptions options,
        bool dryRun,
        CancellationToken ct
    ) =>
        SendJsonAsync<ImportPlan>(
            HttpMethod.Post,
            $"{AppUrl}/import?environment={Escape(environment)}&dryRun={(dryRun ? "true" : "false")}",
            new
            {
                format = options.Format,
                content = options.Content,
                secretKeys = options.SecretKeys,
                onConflict = options.Overwrite ? "overwrite" : "skip",
                exclude = options.Exclude,
            },
            ct
        );
}

internal sealed record ImportOptions(
    string Format,
    string Content,
    IReadOnlyList<string>? SecretKeys,
    bool Overwrite,
    IReadOnlyList<string>? Exclude
);

internal sealed record ImportPlanItem(string Key, string Kind, string Action);

internal sealed record ImportSummary(int Create, int Update, int Unchanged, int Skipped);

internal sealed record ImportPlan(
    IReadOnlyList<ImportPlanItem> Items,
    ImportSummary Summary,
    IReadOnlyList<string> Skipped
);
