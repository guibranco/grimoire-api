using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Interfaces;
using Grimoire.Core.Search;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Controllers.Management;

[ApiController]
[Route("management/search")]
[Tags("Management - Search")]
public class SearchController(IMetadataSearchService search) : ControllerBase
{
    /// <summary>
    /// Finds applications, secrets and configuration keys (application and global) by name, key,
    /// slug or description. Returns where each one lives; never matches on or returns values.
    /// </summary>
    /// <param name="q">Search term, at least 2 characters. Case-insensitive.</param>
    /// <param name="limit">Maximum results (default 20, at most 50).</param>
    /// <param name="ct">Request cancellation.</param>
    [HttpGet]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Get(
        [FromQuery] string? q,
        [FromQuery] int? limit,
        CancellationToken ct
    )
    {
        var term = q?.Trim() ?? string.Empty;
        if (term.Length < SearchRanker.MinQueryLength)
            ModelState.AddModelError(
                nameof(q),
                $"Must be at least {SearchRanker.MinQueryLength} characters."
            );
        if (limit is < 1 or > SearchRanker.MaxLimit)
            ModelState.AddModelError(
                nameof(limit),
                $"Must be between 1 and {SearchRanker.MaxLimit}."
            );
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await search.SearchAsync(term, limit ?? SearchRanker.DefaultLimit, ct);

        Response.Headers.CacheControl = "private, no-store";

        return Ok(
            new SearchResponse(
                result.Query,
                result
                    .Results.Select(h => new SearchResultResponse(
                        h.Type,
                        h.Name,
                        h.Application,
                        h.Environments,
                        h.Description,
                        h.ValueType
                    ))
                    .ToList(),
                result.Truncated
            )
        );
    }
}
