namespace Grimoire.Api.Audit;

/// <summary>Details an action hands to <see cref="AuditedAttribute"/> that it can't read from the request.</summary>
public static class AuditContext
{
    private const string VersionKey = "grimoire.audit.version";

    /// <summary>The secret version created or changed by this request.</summary>
    public static void SetVersion(HttpContext context, int version) =>
        context.Items[VersionKey] = version;

    public static int? GetVersion(HttpContext context) =>
        context.Items.TryGetValue(VersionKey, out var v) && v is int version ? version : null;
}
