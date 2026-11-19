using System.Globalization;
using System.Text.RegularExpressions;

namespace Grimoire.Infrastructure.Backups;

/// <summary>
/// Backup names are <c>grimoire-yyyyMMddTHHmmssfffZ.gbak</c>: they sort chronologically, carry their
/// own timestamp, and anything else in a target directory or bucket prefix is never touched.
/// </summary>
public static partial class BackupNaming
{
    public const string Extension = ".gbak";
    private const string TimestampFormat = "yyyyMMdd'T'HHmmssfff'Z'";

    public static string For(DateTimeOffset timestamp) =>
        $"grimoire-{timestamp.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture)}{Extension}";

    public static bool TryParse(string name, out DateTimeOffset createdAt)
    {
        createdAt = default;
        var match = NamePattern().Match(name);
        if (!match.Success)
            return false;

        if (
            !DateTime.TryParseExact(
                match.Groups["ts"].Value,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out var utc
            )
        )
            return false;

        createdAt = new DateTimeOffset(utc, TimeSpan.Zero);
        return true;
    }

    public static bool IsBackupName(string name) => TryParse(name, out _);

    [GeneratedRegex(@"^grimoire-(?<ts>\d{8}T\d{9}Z)\.gbak$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}
