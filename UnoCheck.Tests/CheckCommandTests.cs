using System.Runtime.InteropServices;
using DotNetCheck;
using DotNetCheck.Cli;

namespace UnoCheck.Tests;

public class CheckCommandTests
{
    [Theory]
    [InlineData("netcoreapp3.1")]
    [InlineData("net481")]
    [InlineData("netstandard2.1")]
    public void ParseTfmsToTargetPlatforms_Returns_Empty_Collection_For_NotSupported_TFM(params string[] tfms)
    {
        var platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings { Frameworks = tfms });
        Assert.Empty(platformsToInclude);
    }

    [Theory]
    [InlineData("net5.0")]
    [InlineData("net6.0")]
    [InlineData("net7.0")]
    [InlineData("net8.0")]
    [InlineData("net9.0")]
    public void ParseTfmsToTargetPlatforms_Returns_Empty_Collection_For_Not_OS_Specific_TFMs(params string[] tfms)
    {
        var platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings { Frameworks = tfms });
        Assert.Empty(platformsToInclude);
    }

    [Fact]
    public void ParseTfmsToTargetPlatforms_Returns_Correct_Values_For_OS_Specific_TFMs()
    {
        var platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings
        {
            Frameworks =
            ["net8.0-windows10.0.19041", "net8.0-android", "net8.0-ios", "net8.0-maccatalyst", "net8.0-browserwasm"]
        });
        Assert.Equal<IEnumerable<string>>(platformsToInclude, ["windows", "android", "ios", "macos", "web"]);

        platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings
        {
            Frameworks =
            ["net8.0-windows10.0.19041", "net8.0-android", "net8.0-ios", "net8.0-maccatalyst", "net8.0-browserwasm", "net8.0-desktop"]
        });
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal<IEnumerable<string>>(platformsToInclude, ["windows", "android", "ios", "macos", "web", "win32"]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Equal<IEnumerable<string>>(platformsToInclude, ["windows", "android", "ios", "macos", "web", "macos"]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Equal<IEnumerable<string>>(platformsToInclude, ["windows", "android", "ios", "macos", "web", "linux"]);
        }
    }

    [Theory]
    [InlineData(new string[] { "net9.0-android", "net9.0-ios", "net9.0-browserwasm", "net9.0-desktop"/*, "net9.0"*/}, new string[] { "android", "ios", "web", "win32" })]
    [InlineData(new string[] { "net9.0-windows10.0.19041", "net9.0-android", "net9.0-ios", "net9.0-maccatalyst", "net9.0-browserwasm" }, new string[] { "windows", "android", "ios", "macos", "web" })]
    public void ParseTfmsToTargetPlatforms_Returns_Correct_Values_For_TFMs(string[] commandLineInput, string[] expectedToBeIncludedTargetPlatforms)
    {
        // Arrange
        CheckSettings settings = new CheckSettings
        {
            Frameworks = commandLineInput
        };

        // Act
        var platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(settings);

        // Assert
        Assert.Equal(expectedToBeIncludedTargetPlatforms, platformsToInclude);
    }
}

