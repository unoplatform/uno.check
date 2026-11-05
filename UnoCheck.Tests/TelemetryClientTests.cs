using DotNetCheck;

namespace UnoCheck.Tests;

public class TelemetryClientTests
{
    [Fact]
    public void FormatRequestedFrameworks_KeepsShortFrameworks_WhenTfmIsPassed()
    {
        // Arrange: real TFMs are shorter than the 32-character cap, which used to throw
        string[] frameworks = ["net9.0-ios", "net9.0-android"];

        // Act
        var result = TelemetryClient.FormatRequestedFrameworks(frameworks);

        // Assert
        Assert.Equal("net9.0-android,net9.0-ios", result);
    }

    [Fact]
    public void FormatRequestedFrameworks_CutsFrameworksLongerThan32Characters()
    {
        // Arrange
        var longFramework = "net9.0-" + new string('a', 40);

        // Act
        var result = TelemetryClient.FormatRequestedFrameworks([longFramework]);

        // Assert
        Assert.Equal(longFramework[..32], result);
    }

    [Fact]
    public void FormatRequestedFrameworks_DropsValuesThatAreNotTfms()
    {
        // Act
        var result = TelemetryClient.FormatRequestedFrameworks(["net8.0", "android", "net8.0-windows10.0.19041", "netstandard2.0"]);

        // Assert
        Assert.Equal("net8.0,net8.0-windows10.0.19041", result);
    }

    [Fact]
    public void FormatRequestedFrameworks_KeepsAtMostTenFrameworks()
    {
        // Arrange: reversed, so keeping 10 before sorting would keep net15.0 to net24.0
        var frameworks = Enumerable.Range(10, 15).Reverse().Select(i => $"net{i}.0").ToArray();

        // Act
        var result = TelemetryClient.FormatRequestedFrameworks(frameworks);

        // Assert
        Assert.Equal("net10.0,net11.0,net12.0,net13.0,net14.0,net15.0,net16.0,net17.0,net18.0,net19.0", result);
    }

    [Fact]
    public void FormatRequestedFrameworks_ReturnsEmpty_WhenNoTfmIsPassed()
    {
        // Act
        var result = TelemetryClient.FormatRequestedFrameworks(null!);

        // Assert
        Assert.Equal("", result);
    }
}
