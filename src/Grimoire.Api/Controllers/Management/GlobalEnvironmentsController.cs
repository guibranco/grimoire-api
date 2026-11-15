using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Interfaces.Repositories;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/global/environments")]
[Tags("Management - Global Configurations")]
public class GlobalEnvironmentsController(IEnvironmentRepository envRepo) : ControllerBase
{
    /// <summary>
    /// Distinct environment slugs across all non-deleted applications, for picking the
    /// target of a global value. When applications name the same slug differently, the most
    /// common name is returned.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var environments = await envRepo.GetAllForActiveApplicationsAsync(ct);
        var result = environments
            .GroupBy(e => e.Slug, StringComparer.Ordinal)
            .Select(g => new GlobalEnvironmentResponse(
                g.Key,
                g.GroupBy(e => e.Name)
                    .OrderByDescending(n => n.Count())
                    .ThenBy(n => n.Key, StringComparer.Ordinal)
                    .First()
                    .Key,
                g.Select(e => e.ApplicationId).Distinct().Count()
            ))
            .OrderBy(e => e.Slug, StringComparer.Ordinal);
        return Ok(result);
    }
}
