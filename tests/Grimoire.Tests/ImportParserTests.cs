using Grimoire.Core.Entities;
using Grimoire.Core.Import;

namespace Grimoire.Tests;

public class ImportParserTests
{
    private static Dictionary<string, ImportedValue> Parse(ImportFormat format, string content) =>
        ImportParser.Parse(format, content).Values.ToDictionary(v => v.Key);

    [Fact]
    public void Env_ReadsQuotedUnquotedAndExportedValues()
    {
        var values = Parse(
            ImportFormat.Env,
            """
            # comment
            export DB_HOST=localhost
            DB_PASSWORD="p@ss \"quoted\" # not a comment"
            API_URL='https://x.example/$HOME'   
            PLAIN = value with spaces # trailing comment
            EMPTY=
            MULTI="line one
            line two"
            ESCAPED="a\nb"
            DB_HOST=override
            """
        );

        Assert.Equal("override", values["DB_HOST"].Value);
        Assert.Equal("p@ss \"quoted\" # not a comment", values["DB_PASSWORD"].Value);
        Assert.Equal("https://x.example/$HOME", values["API_URL"].Value);
        Assert.Equal("value with spaces", values["PLAIN"].Value);
        Assert.Equal("", values["EMPTY"].Value);
        Assert.Equal("line one\nline two", values["MULTI"].Value);
        Assert.Equal(ConfigurationValueType.Multiline, values["MULTI"].Type);
        Assert.Equal("a\nb", values["ESCAPED"].Value);
        Assert.Equal(7, values.Count);
    }

    [Theory]
    [InlineData("NOT AN ASSIGNMENT", "Line 1: expected KEY=value.")]
    [InlineData("A=\"open", "Line 1: unterminated double quote.")]
    [InlineData("A='open", "Line 1: unterminated single quote.")]
    public void Env_ReportsTheBadLine(string content, string message)
    {
        var ex = Assert.Throws<ImportParseException>(() =>
            ImportParser.Parse(ImportFormat.Env, content)
        );
        Assert.Equal(message, ex.Message);
    }

    [Fact]
    public void AppSettings_FlattensSectionsAndTypesArrays()
    {
        var values = Parse(
            ImportFormat.AppSettings,
            """
            {
              // comments and trailing commas are fine
              "ConnectionStrings": { "Default": "Server=db" },
              "Logging": { "LogLevel": { "Default": "Information" } },
              "AllowedHosts": "*",
              "Origins": ["https://a", "https://b"],
              "Retries": 3,
              "Enabled": true,
              "Optional": null,
              "Endpoints": [{ "Url": "x" }],
            }
            """
        );

        Assert.Equal("Server=db", values["ConnectionStrings:Default"].Value);
        Assert.Equal("Information", values["Logging:LogLevel:Default"].Value);
        Assert.Equal("""["https://a","https://b"]""", values["Origins"].Value);
        Assert.Equal(ConfigurationValueType.Array, values["Origins"].Type);
        Assert.Equal("3", values["Retries"].Value);
        Assert.Equal("true", values["Enabled"].Value);
        Assert.Equal("", values["Optional"].Value);
        Assert.Equal("""[{"Url":"x"}]""", values["Endpoints"].Value);
        Assert.Equal(ConfigurationValueType.Json, values["Endpoints"].Type);
    }

    [Fact]
    public void Json_KeepsTopLevelKeysAndObjectsAsJson()
    {
        var values = Parse(ImportFormat.Json, """{ "Mode": "safe", "Limits": { "Max": 5 } }""");
        Assert.Equal("safe", values["Mode"].Value);
        Assert.Equal("""{"Max":5}""", values["Limits"].Value);
        Assert.Equal(ConfigurationValueType.Json, values["Limits"].Type);
    }

    [Theory]
    [InlineData(ImportFormat.Json, "[1, 2]", "Expected a JSON object at the top level.")]
    [InlineData(ImportFormat.AppSettings, "{ nope", "Invalid JSON")]
    public void Json_RejectsWhatIsNotAnObject(ImportFormat format, string content, string message)
    {
        var ex = Assert.Throws<ImportParseException>(() => ImportParser.Parse(format, content));
        Assert.StartsWith(message, ex.Message);
    }

    [Fact]
    public void Php_ReadsReturnArraysAsData()
    {
        var result = ImportParser.Parse(
            ImportFormat.Php,
            """
            <?php
            // cPanel-style config
            declare(strict_types=1);
            return [
                'db' => [
                    'host' => 'localhost',
                    "motto" => "say \"hi\"\n",
                    'port' => 3306,
                ],
                'debug' => false,
                'timeout' => -1.5,
                'name' => 'My ' . 'App',
                'hosts' => ['a', 'b'],
                'empty' => null,
                'path' => __DIR__ . '/cache',
                'key' => getenv('APP_KEY'),
                /* block
                   comment */
                'legacy' => array('x' => 1),
                'routes' => [['path' => '/'], ['path' => '/about']],
            ];
            """
        );
        var values = result.Values.ToDictionary(v => v.Key);

        Assert.Equal("localhost", values["db:host"].Value);
        Assert.Equal("say \"hi\"\n", values["db:motto"].Value);
        Assert.Equal("3306", values["db:port"].Value);
        Assert.Equal("false", values["debug"].Value);
        Assert.Equal("-1.5", values["timeout"].Value);
        Assert.Equal("My App", values["name"].Value);
        Assert.Equal("""["a","b"]""", values["hosts"].Value);
        Assert.Equal(ConfigurationValueType.Array, values["hosts"].Type);
        Assert.Equal("", values["empty"].Value);
        Assert.Equal("1", values["legacy:x"].Value);
        Assert.Equal("""[{"path":"/"},{"path":"/about"}]""", values["routes"].Value);
        Assert.Equal(ConfigurationValueType.Json, values["routes"].Type);
        Assert.Equal(["path", "key"], result.Skipped);
    }

    [Fact]
    public void Php_ReadsDefinesAndAssignments()
    {
        var values = Parse(
            ImportFormat.Php,
            """
            <?php
            define('DB_NAME', 'wordpress');
            define("WP_DEBUG", true);
            $config['mail']['from'] = 'no-reply@example.com';
            $config = ['site' => 'Example'];
            $unrelated++;
            if (true) { echo 'never run'; }
            """
        );

        Assert.Equal("wordpress", values["DB_NAME"].Value);
        Assert.Equal("true", values["WP_DEBUG"].Value);
        Assert.Equal("no-reply@example.com", values["mail:from"].Value);
        Assert.Equal("Example", values["site"].Value);
        Assert.Equal(4, values.Count);
    }

    [Fact]
    public void Php_ReportsUnterminatedStrings()
    {
        var ex = Assert.Throws<ImportParseException>(() =>
            ImportParser.Parse(ImportFormat.Php, "<?php\nreturn ['a' => 'open];")
        );
        Assert.Equal("Line 2: unterminated string.", ex.Message);
    }

    [Theory]
    [InlineData("env", true)]
    [InlineData("AppSettings", true)]
    [InlineData("yaml", false)]
    [InlineData(null, false)]
    public void Formats_AreMatchedByName(string? name, bool known) =>
        Assert.Equal(known, ImportParser.TryParseFormat(name, out _));
}
