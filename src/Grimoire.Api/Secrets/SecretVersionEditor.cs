using Grimoire.Api.Audit;
using Grimoire.Api.DTOs.Management;
using Grimoire.Core.Secrets;
using Microsoft.AspNetCore.Mvc;

namespace Grimoire.Api.Secrets;

/// <summary>Version listing and window editing shared by application and Global secrets.</summary>
internal static class SecretVersionEditor
{
    public static List<SecretVersionMetadata> ToMetadata<T>(
        IReadOnlyCollection<T> versions,
        DateTimeOffset now
    )
        where T : class, IEditableSecretVersion
    {
        var current = SecretVersionRules.Current(versions, now)?.Version;
        return versions
            .OrderByDescending(v => v.Version)
            .Select(v => new SecretVersionMetadata(
                v.Id,
                v.Version,
                v.IsEnabled,
                v.ExpiresAt,
                v.NotBefore,
                v.CreatedAt,
                v.Version == current,
                SecretVersionRules.Status(v, current, now)
            ))
            .ToList();
    }

    /// <summary>
    /// Applies <paramref name="request"/> to version <paramref name="version"/>: 404 when it
    /// doesn't exist, 400 when the window is inverted, 409 when the change would leave no valid
    /// version and <paramref name="force"/> is off. Returns the updated version's metadata.
    /// </summary>
    public static async Task<IActionResult> UpdateAsync<T>(
        ControllerBase controller,
        IReadOnlyCollection<T> versions,
        int version,
        UpdateSecretVersionRequest request,
        bool force,
        Func<CancellationToken, Task> save,
        CancellationToken ct
    )
        where T : class, IEditableSecretVersion
    {
        var target = versions.FirstOrDefault(v => v.Version == version);
        if (target is null)
            return controller.NotFound(
                controller.Problem("Secret version not found.", statusCode: 404)
            );

        var isEnabled = request.IsEnabled ?? target.IsEnabled;
        var notBefore = request.ClearNotBefore ? null : request.NotBefore ?? target.NotBefore;
        var expiresAt = request.ClearExpiresAt ? null : request.ExpiresAt ?? target.ExpiresAt;

        if (expiresAt is { } e && notBefore is { } n && e <= n)
        {
            controller.ModelState.AddModelError(
                nameof(UpdateSecretVersionRequest.ExpiresAt),
                "Must be after 'notBefore'."
            );
            return controller.ValidationProblem(controller.ModelState);
        }

        var now = DateTimeOffset.UtcNow;
        if (
            !force
            && SecretVersionRules.WouldLeaveNoValidVersion(
                versions,
                target,
                isEnabled,
                notBefore,
                expiresAt,
                now
            )
        )
            return controller.Conflict(
                controller.Problem(
                    "This change would leave the secret with no valid version in this environment. "
                        + "Add a new value first, or repeat the request with force=true.",
                    statusCode: 409
                )
            );

        target.SetWindow(isEnabled, notBefore, expiresAt);
        await save(ct);
        AuditContext.SetVersion(controller.HttpContext, version);

        return controller.Ok(ToMetadata(versions, now).Single(v => v.Version == version));
    }
}
