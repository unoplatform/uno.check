using DotNetCheck.DotNet;

namespace UnoCheck.Tests;

public class DotNetHomebrewDetectorTests
{
    [Theory]
    [InlineData("/opt/homebrew/Cellar/dotnet/9.0.100")]
    [InlineData("/opt/homebrew/bin/dotnet")]
    [InlineData("/usr/local/Cellar/dotnet/8.0.100")]
    public void IsHomebrewInstall_ReturnsTrue_ForHomebrewPaths(string path)
    {
        Assert.True(DotNetHomebrewDetector.IsHomebrewInstall(path));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/usr/local/share/dotnet")]
    [InlineData("C:/Program Files/dotnet")]
    public void IsHomebrewInstall_ReturnsFalse_ForNonHomebrewPaths(string? path)
    {
        Assert.False(DotNetHomebrewDetector.IsHomebrewInstall(path));
    }
}
