namespace Grimoire.Core.Entities;

/// <summary>How a configuration value is shaped, so consumers know how to read it.</summary>
public enum ConfigurationValueType
{
    /// <summary>A single-line string (the default, and the behaviour before value types existed).</summary>
    String,

    /// <summary>Free text with line breaks preserved (line endings are normalised to <c>\n</c>).</summary>
    Multiline,

    /// <summary>An ordered list of strings, stored as a minified JSON array.</summary>
    Array,

    /// <summary>A JSON object or array, stored minified.</summary>
    Json,
}
