using System.Reflection;

namespace Grimoire.Api;

/// <summary>
/// The running build's version, taken from the assembly's informational version.
/// Set it at build time with <c>-p:Version=1.2.3</c> (the deploy workflow passes the
/// GitVersion FullSemVer). Build metadata such as a <c>+sha</c> suffix is dropped, so the
/// value matches the release tag (e.g. <c>1.2.3</c>), the same as WebhooksHandler reports.
/// </summary>
public static class AppVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = typeof(AppVersion).Assembly;
        return Format(
            assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion,
            assembly.GetName().Version
        );
    }

    internal static string Format(string? informational, Version? assemblyVersion)
    {
        if (string.IsNullOrWhiteSpace(informational))
            return assemblyVersion?.ToString(3) ?? "dev";

        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
