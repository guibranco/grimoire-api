using System.Text;
using System.Text.Json;
using Grimoire.Api.AccessTokens;
using Grimoire.Api.Audit;
using Grimoire.Api.ConsumerTracking;
using Grimoire.Api.DTOs.Consumer;
using Grimoire.Api.Middleware;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Export;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Grimoire.Core.Publishing;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Consumer;

/// <summary>
/// Everything an application can read in one environment, in one call: for the GitHub Action,
/// non-.NET clients, the CLI and <c>.env</c> generation (guibranco/grimoire-api#74).
/// </summary>
[ApiController]
[TrackConsumerActivity]
[Route("consumer/export")]
[Tags("Consumer - Export")]
public class ConsumerExportController(
    IEnvironmentRepository envRepo,
    IEffectiveConfigurationService effective,
    IEncryptionService encryption,
    ISecretReadTracker secretReads
) : ControllerBase
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Exports the effective (merged) values of <paramref name="environment"/>.
    /// </summary>
    /// <param name="environment">Environment slug.</param>
    /// <param name="format"><c>json</c> (default), <c>env</c> or <c>dotnet</c>.</param>
    /// <param name="include">Comma-separated <c>secrets</c>, <c>configurations</c>; both by default.</param>
    /// <param name="prefix">Only keys and secret names starting with this (ignoring case).</param>
    /// <param name="ct">Cancellation token.</param>
    [Audited(AuditActions.ExportRead)]
    [HttpGet]
    [ProducesResponseType<ExportJsonResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Export(
        [FromQuery] string environment,
        [FromQuery] string? format,
        [FromQuery] string? include,
        [FromQuery] string? prefix,
        CancellationToken ct
    )
    {
        Response.Headers.CacheControl = "no-store";
        if (HttpContext.Items[ConsumerApiKeyMiddleware.AppContextKey] is not Application app)
            return Unauthorized();
        if (!ExportFormatter.TryParseFormat(format, out var exportFormat))
            return Problem(
                $"Unknown format. Use one of: {string.Join(", ", ExportFormatter.Formats)}.",
                statusCode: 400
            );
        if (!AccessTokenScopeNames.TryParseExportInclude(include, out var scopes))
            return Problem(
                "Unknown include. Use secrets, configurations or both, comma-separated.",
                statusCode: 400
            );

        var env = await envRepo.GetBySlugAsync(app.Id, environment, ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);
        HttpContext.Items[ConsumerApiKeyMiddleware.EnvironmentContextKey] = env;
        AuditContext.SetTarget(HttpContext, exportFormat.ToString().ToLowerInvariant());

        var configurations = scopes.HasFlag(AccessTokenScopes.ReadConfigurations)
            ? await ConfigurationsAsync(app, env, prefix, ct)
            : new Dictionary<string, string>();
        var (secrets, skipped, read) = scopes.HasFlag(AccessTokenScopes.ReadSecrets)
            ? await SecretsAsync(app, env, prefix, ct)
            : (new Dictionary<string, string>(), [], []);

        var (body, contentType) = Render(exportFormat, secrets, configurations, skipped);

        // Only a configuration-only export can be polled: secret values aren't fingerprinted.
        if (scopes == AccessTokenScopes.ReadConfigurations && ConsumerEtag.IsCurrent(this, body))
            return StatusCode(StatusCodes.Status304NotModified);

        foreach (var version in read)
            secretReads.Record(app.Id, env.Id, version.Source, version.SecretId);
        return File(body, contentType);
    }

    private async Task<Dictionary<string, string>> ConfigurationsAsync(
        Application app,
        AppEnvironment env,
        string? prefix,
        CancellationToken ct
    ) =>
        (await effective.GetConfigurationAsync(app, env, ct))
            .Where(e => Matches(e.Key, prefix) && !ConfigurationSentinel.IsSentinel(e.Key))
            .ToDictionary(e => e.Key, e => e.Value, StringComparer.Ordinal);

    private async Task<(
        Dictionary<string, string> Values,
        List<string> Skipped,
        List<ResolvedSecretVersion> Read
    )> SecretsAsync(Application app, AppEnvironment env, string? prefix, CancellationToken ct)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var skipped = new List<string>();
        var read = new List<ResolvedSecretVersion>();
        var now = DateTimeOffset.UtcNow;
        var names = (await effective.GetSecretsAsync(app, ct))
            .Select(secret => secret.Name)
            .Where(name => Matches(name, prefix));
        foreach (var name in names)
        {
            var resolution = await effective.ResolveSecretAsync(app, env, name, now, ct);
            if (resolution.Value is not { } version)
            {
                skipped.Add(name);
                continue;
            }
            values[name] = encryption.Decrypt(version.EncryptedValue);
            read.Add(version);
        }
        return (values, skipped, read);
    }

    private static bool Matches(string key, string? prefix) =>
        string.IsNullOrEmpty(prefix) || key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static (byte[] Body, string ContentType) Render(
        ExportFormat format,
        Dictionary<string, string> secrets,
        Dictionary<string, string> configurations,
        List<string> skipped
    )
    {
        var values = new ExportedValues(secrets, configurations);
        return format switch
        {
            ExportFormat.Env => (
                Encoding.UTF8.GetBytes(ExportFormatter.ToEnv(values)),
                "text/plain; charset=utf-8"
            ),
            ExportFormat.Dotnet => (
                JsonSerializer.SerializeToUtf8Bytes(ExportFormatter.ToDotnet(values), JsonOptions),
                JsonContentType
            ),
            _ => (
                JsonSerializer.SerializeToUtf8Bytes(
                    new ExportJsonResponse(
                        new SortedDictionary<string, string>(secrets, StringComparer.Ordinal),
                        new SortedDictionary<string, string>(
                            configurations,
                            StringComparer.Ordinal
                        ),
                        skipped
                    ),
                    JsonOptions
                ),
                JsonContentType
            ),
        };
    }
}
