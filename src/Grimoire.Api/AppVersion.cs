using System.Reflection;

namespace Grimoire.Api;

/// <summary>
/// The running build's version, taken from the assembly's informational version.
/// Set it at build time with <c>-p:Version=1.2.3</c>; the SDK appends the git commit
/// as <c>+sha</c>, which is shortened to 7 characters here (e.g. <c>1.2.3+632d7f0</c>).
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
        if (plus < 0 || informational.Length - plus - 1 <= 7)
            return informational;

        return informational[..(plus + 8)];
    }
}
