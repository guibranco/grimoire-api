using System.Collections;
using System.Globalization;

namespace Grimoire.Consumer.FeatureFlags;

/// <summary>
/// Attributes a flag is evaluated against, e.g. <c>userId</c>, <c>email</c>, <c>country</c>,
/// <c>appVersion</c>. Evaluation is local: the context never leaves the process.
/// </summary>
/// <example><code>var context = new FlagContext("u-123").With("country", "IE").With("beta", true);</code></example>
public sealed class FlagContext : IReadOnlyDictionary<string, string>
{
    /// <summary>The attribute rollouts bucket by unless a rule says otherwise.</summary>
    public const string UserIdAttribute = "userId";

    private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>An empty context: rules on attributes never match and rollouts serve the default variant.</summary>
    public static FlagContext Empty => new();

    public FlagContext() { }

    /// <param name="userId">Sets the <c>userId</c> attribute (when not null or empty).</param>
    public FlagContext(string? userId)
    {
        if (!string.IsNullOrEmpty(userId))
            _values[UserIdAttribute] = userId;
    }

    /// <summary>Sets a string attribute; <c>null</c> removes it.</summary>
    public FlagContext With(string attribute, string? value)
    {
        if (value is null)
            _values.Remove(attribute);
        else
            _values[attribute] = value;
        return this;
    }

    /// <summary>Sets a boolean attribute (compared as <c>true</c> / <c>false</c>).</summary>
    public FlagContext With(string attribute, bool value) =>
        With(attribute, value ? "true" : "false");

    /// <summary>Sets a numeric attribute (compared as its invariant text, e.g. <c>42</c>, <c>1.5</c>).</summary>
    public FlagContext With(string attribute, double value) =>
        With(attribute, value.ToString(CultureInfo.InvariantCulture));

    /// <inheritdoc cref="With(string, double)"/>
    public FlagContext With(string attribute, long value) =>
        With(attribute, value.ToString(CultureInfo.InvariantCulture));

    public string this[string key] => _values[key];
    public IEnumerable<string> Keys => _values.Keys;
    public IEnumerable<string> Values => _values.Values;
    public int Count => _values.Count;

    public bool ContainsKey(string key) => _values.ContainsKey(key);

    public bool TryGetValue(string key, out string value) => _values.TryGetValue(key, out value!);

    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _values.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
