using Grimoire.Infrastructure.Services;
using Xunit;

namespace Grimoire.Tests;

public class SlugServiceTests
{
    [Theory]
    [InlineData("My Application", "my-application")]
    [InlineData("Hello World", "hello-world")]
    [InlineData("ALL CAPS", "all-caps")]
    [InlineData("already-slug", "already-slug")]
    [InlineData("Special!@#Chars", "specialchars")]
    [InlineData("Multiple   Spaces", "multiple-spaces")]
    [InlineData("  Leading Trailing  ", "leading-trailing")]
    [InlineData("123 Numbers", "123-numbers")]
    [InlineData("a---b---c", "a-b-c")]
    public void Generate_ProducesExpectedSlug(string input, string expected)
    {
        var result = SlugService.Generate(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void Generate_LowercasesAllCharacters()
    {
        var result = SlugService.Generate("ABC DEF");
        Assert.Equal(result, result.ToLowerInvariant());
    }

    [Fact]
    public void Generate_NoLeadingOrTrailingDashes()
    {
        var result = SlugService.Generate("  test  ");
        Assert.False(result.StartsWith('-'));
        Assert.False(result.EndsWith('-'));
    }

    [Fact]
    public void Generate_NoDuplicateDashes()
    {
        var result = SlugService.Generate("a   b   c");
        Assert.DoesNotContain("--", result);
    }
}
