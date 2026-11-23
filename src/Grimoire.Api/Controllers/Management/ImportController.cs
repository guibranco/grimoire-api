using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Api.Import;
using Grimoire.Api.Validators.Management;
using Grimoire.Core.Audit;
using Grimoire.Core.Entities;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

/// <summary>
/// Imports a dotenv, appsettings.json, PHP config or JSON file into one environment
/// (guibranco/grimoire-api#82). Keys matching the secret globs become secrets; the rest become
/// configuration. A dry run (the default) only returns the plan.
/// </summary>
[ApiController]
[Tags("Management - Import")]
public class ImportController(Importer importer, IEncryptionService encryption) : ControllerBase
{
    /// <summary>Imports into an application environment.</summary>
    /// <param name="slug">Application slug.</param>
    /// <param name="environment">Environment slug.</param>
    /// <param name="req">The file and how to classify it.</param>
    /// <param name="dryRun">Only return the plan (default). <c>false</c> applies it in one transaction.</param>
    /// <param name="apps">Applications.</param>
    /// <param name="envs">Environments.</param>
    /// <param name="configs">Configuration entries.</param>
    /// <param name="secrets">Secrets.</param>
    /// <param name="ct">Request cancellation.</param>
    [Audited(AuditActions.ImportApply)]
    [HttpPost("management/applications/{slug}/import")]
    [ProducesResponseType<ImportPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ImportApplication(
        string slug,
        [FromQuery] string environment,
        [FromBody] ImportRequest req,
        [FromServices] IApplicationRepository apps,
        [FromServices] IEnvironmentRepository envs,
        [FromServices] IConfigurationRepository configs,
        [FromServices] ISecretRepository secrets,
        CancellationToken ct,
        [FromQuery] bool dryRun = true
    )
    {
        var app = await apps.GetBySlugAsync(slug, ct);
        if (app is null)
            return Problem(ProblemMessages.ApplicationNotFound, statusCode: 404);
        var env = await envs.GetBySlugAsync(app.Id, environment ?? "", ct);
        if (env is null)
            return Problem(ProblemMessages.EnvironmentNotFound, statusCode: 404);

        return await RunAsync(
            new ApplicationImportTarget(app, env, configs, secrets, encryption),
            env.Slug,
            req,
            dryRun,
            ct
        );
    }

    /// <summary>Imports into a global environment (<c>*</c> for all environments).</summary>
    /// <param name="environment">Global environment slug, or <c>*</c>.</param>
    /// <param name="req">The file and how to classify it.</param>
    /// <param name="dryRun">Only return the plan (default). <c>false</c> applies it in one transaction.</param>
    /// <param name="configs">Global configuration entries.</param>
    /// <param name="secrets">Global secrets.</param>
    /// <param name="ct">Request cancellation.</param>
    [Audited(AuditActions.ImportApply)]
    [HttpPost("management/global/import")]
    [ProducesResponseType<ImportPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ImportGlobal(
        [FromQuery] string environment,
        [FromBody] ImportRequest req,
        [FromServices] IGlobalConfigurationRepository configs,
        [FromServices] IGlobalSecretRepository secrets,
        CancellationToken ct,
        [FromQuery] bool dryRun = true
    )
    {
        if (!GlobalEnvironmentSlug.IsValid(environment))
        {
            ModelState.AddModelError(
                nameof(environment),
                GlobalEnvironmentSlug.Message.Replace("{PropertyName}", "Environment")
            );
            return ValidationProblem(ModelState);
        }
        return await RunAsync(
            new GlobalImportTarget(environment, configs, secrets, encryption),
            environment,
            req,
            dryRun,
            ct
        );
    }

    private async Task<IActionResult> RunAsync(
        IImportTarget target,
        string environment,
        ImportRequest req,
        bool dryRun,
        CancellationToken ct
    )
    {
        if (dryRun)
            AuditContext.Skip(HttpContext);
        AuditContext.SetTarget(HttpContext, req.Format.ToLowerInvariant());

        var (plan, error) = await importer.RunAsync(target, environment, req, dryRun, ct);
        if (error is not null)
        {
            ModelState.AddModelError(nameof(req.Content), error);
            return ValidationProblem(ModelState);
        }
        return Ok(plan);
    }
}
