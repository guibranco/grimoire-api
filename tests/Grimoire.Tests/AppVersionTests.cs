using Grimoire.Api;

namespace Grimoire.Tests;

public class AppVersionTests
{
    [Fact]
    public void Current_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Format_WithoutInformationalVersion_FallsBackToAssemblyVersion(string? informational)
    {
        Assert.Equal("1.2.3", AppVersion.Format(informational, new Version(1, 2, 3, 4)));
    }

    [Fact]
    public void Format_WithoutAnyVersion_ReturnsDev()
    {
        Assert.Equal("dev", AppVersion.Format(null, null));
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta.4", "1.2.3-beta.4")]
    [InlineData("1.2.3+632d7f0", "1.2.3")]
    [InlineData("1.2.3-beta.4+632d7f0a1b2c3d4e5f60718293a4b5c6d7e8f90", "1.2.3-beta.4")]
    public void Format_DropsBuildMetadata(string informational, string expected)
    {
        Assert.Equal(expected, AppVersion.Format(informational, null));
    }
}
