using System.Globalization;
using System.Text.RegularExpressions;

// Shared with Grimoire.Consumer (linked source, built with GRIMOIRE_CONSUMER): one evaluator for
// the server's dry run and the client library, both checked against the shared test vectors.
#if GRIMOIRE_CONSUMER
namespace Grimoire.Consumer.FeatureFlags.Evaluation;

#else
namespace Grimoire.Core.FeatureFlags;

#endif

/// <summary>
/// Semantic Versioning 2.0.0 parsing and precedence (build metadata ignored). A leading <c>v</c>
/// is accepted; missing minor/patch are not.
/// </summary>
public sealed partial record SemVer(
    int Major,
    int Minor,
    int Patch,
    IReadOnlyList<string> PreRelease
) : IComparable<SemVer>
{
    [GeneratedRegex(
        @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+[0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*)?$",
        RegexOptions.CultureInvariant,
        matchTimeoutMilliseconds: 100
    )]
    private static partial Regex Pattern();

    public static bool TryParse(string? text, out SemVer version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var m = Pattern().Match(text.Trim());
        if (
            !m.Success
            || !int.TryParse(
                m.Groups[1].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var major
            )
            || !int.TryParse(
                m.Groups[2].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var minor
            )
            || !int.TryParse(
                m.Groups[3].Value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var patch
            )
        )
            return false;
        version = new SemVer(
            major,
            minor,
            patch,
            m.Groups[4].Success ? m.Groups[4].Value.Split('.') : []
        );
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other is null)
            return 1;
        var c = Major.CompareTo(other.Major);
        if (c == 0)
            c = Minor.CompareTo(other.Minor);
        if (c == 0)
            c = Patch.CompareTo(other.Patch);
        if (c != 0)
            return Math.Sign(c);

        // A pre-release has lower precedence than the release.
        if (PreRelease.Count == 0 || other.PreRelease.Count == 0)
            return Math.Sign(other.PreRelease.Count.CompareTo(PreRelease.Count));

        for (var i = 0; i < Math.Min(PreRelease.Count, other.PreRelease.Count); i++)
        {
            c = CompareIdentifier(PreRelease[i], other.PreRelease[i]);
            if (c != 0)
                return c;
        }
        return Math.Sign(PreRelease.Count.CompareTo(other.PreRelease.Count));
    }

    public static bool operator <(SemVer? left, SemVer? right) => Compare(left, right) < 0;

    public static bool operator <=(SemVer? left, SemVer? right) => Compare(left, right) <= 0;

    public static bool operator >(SemVer? left, SemVer? right) => Compare(left, right) > 0;

    public static bool operator >=(SemVer? left, SemVer? right) => Compare(left, right) >= 0;

    /// <summary>Precedence order with <c>null</c> lowest.</summary>
    private static int Compare(SemVer? left, SemVer? right)
    {
        if (left is not null)
            return left.CompareTo(right);
        return right is null ? 0 : -1;
    }

    private static int CompareIdentifier(string a, string b)
    {
        var aNum = long.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var an);
        var bNum = long.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out var bn);
        if (aNum && bNum)
            return Math.Sign(an.CompareTo(bn));
        if (aNum)
            return -1;
        if (bNum)
            return 1;
        return Math.Sign(string.CompareOrdinal(a, b));
    }
}
