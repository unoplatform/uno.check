using System.Runtime.InteropServices;
using DotNetCheck;
using DotNetCheck.Cli;
using DotNetCheck.Models;

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
        var platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings { Frameworks =
            ["net8.0-windows10.0.19041", "net8.0-android", "net8.0-ios", "net8.0-maccatalyst", "net8.0-browserwasm"]
        });
        Assert.Equal(platformsToInclude, ["windows", "android", "ios", "macos", "web"]);
        
        platformsToInclude = CheckCommand.ParseTfmsToTargetPlatforms(new CheckSettings { Frameworks =
            ["net8.0-windows10.0.19041", "net8.0-android", "net8.0-ios", "net8.0-maccatalyst", "net8.0-browserwasm", "net8.0-desktop"]
        });
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal(platformsToInclude, ["windows", "android", "ios", "macos", "web", "win32"]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Equal(platformsToInclude, ["windows", "android", "ios", "macos", "web", "macos"]);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Equal(platformsToInclude, ["windows", "android", "ios", "macos", "web", "linux"]);
        }
    }
}

public class DiagnosticResultTests
{
    private class TestCheckup : Checkup
    {
        public override string Id => "test";
        public override string Title => "Test";
        public override Task<DiagnosticResult> Examine(SharedState history) => Task.FromResult(DiagnosticResult.Ok(this));
    }

    [Fact]
    public void DiagnosticResult_WithErrorAndMessage_HasNoSuggestion()
    {
        // Arrange
        var checkup = new TestCheckup();
        var message = "Unable to find Java";
        
        // Act
        var result = new DiagnosticResult(Status.Error, checkup, message);
        
        // Assert
        Assert.Equal(Status.Error, result.Status);
        Assert.Equal(message, result.Message);
        Assert.False(result.HasSuggestion);
    }

    [Fact]
    public void DiagnosticResult_WithWarningAndMessage_HasNoSuggestion()
    {
        // Arrange
        var checkup = new TestCheckup();
        var message = "Warning message";
        
        // Act
        var result = new DiagnosticResult(Status.Warning, checkup, message);
        
        // Assert
        Assert.Equal(Status.Warning, result.Status);
        Assert.Equal(message, result.Message);
        Assert.False(result.HasSuggestion);
    }

    [Fact]
    public void DiagnosticResult_WithSuggestion_HasSuggestion()
    {
        // Arrange
        var checkup = new TestCheckup();
        var suggestion = new Suggestion("Fix it");
        
        // Act
        var result = new DiagnosticResult(Status.Error, checkup, suggestion);
        
        // Assert
        Assert.Equal(Status.Error, result.Status);
        Assert.True(result.HasSuggestion);
        Assert.Null(result.Message);
    }

    [Fact]
    public void DiagnosticResult_WithEmptyMessage_MessageIsEmpty()
    {
        // Arrange
        var checkup = new TestCheckup();
        
        // Act
        var result = new DiagnosticResult(Status.Error, checkup, "");
        
        // Assert
        Assert.Equal(Status.Error, result.Status);
        Assert.Empty(result.Message);
        Assert.False(result.HasSuggestion);
    }

    [Fact]
    public void DiagnosticResult_Ok_HasOkStatus()
    {
        // Arrange
        var checkup = new TestCheckup();
        
        // Act
        var result = DiagnosticResult.Ok(checkup);
        
        // Assert
        Assert.Equal(Status.Ok, result.Status);
        Assert.Null(result.Message);
        Assert.False(result.HasSuggestion);
    }
}
