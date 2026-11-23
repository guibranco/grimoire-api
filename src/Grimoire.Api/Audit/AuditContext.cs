namespace Grimoire.Api.Audit;

/// <summary>Details an action hands to <see cref="AuditedAttribute"/> that it can't read from the request.</summary>
public static class AuditContext
{
    private const string VersionKey = "grimoire.audit.version";
    private const string TargetKey = "grimoire.audit.target";

    /// <summary>The secret version created or changed by this request.</summary>
    public static void SetVersion(HttpContext context, int version) =>
        context.Items[VersionKey] = version;

    public static int? GetVersion(HttpContext context) =>
        context.Items.TryGetValue(VersionKey, out var v) && v is int version ? version : null;

    /// <summary>The entry acted on, when the route only has its id (e.g. an access token's name).</summary>
    public static void SetTarget(HttpContext context, string target) =>
        context.Items[TargetKey] = target;

    public static string? GetTarget(HttpContext context) =>
        context.Items.TryGetValue(TargetKey, out var t) ? t as string : null;
}
