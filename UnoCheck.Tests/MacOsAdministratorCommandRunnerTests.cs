using DotNetCheck;

namespace UnoCheck.Tests;

public class MacOsAdministratorCommandRunnerTests
{
    [Fact]
    public void BuildCommandLine_QuotesExecutableAndEveryArgument()
    {
        var command = MacOsAdministratorCommandRunner.BuildCommandLine(
            "/Users/Test User/.dotnet/dotnet",
            ["workload", "install", "value with spaces", "$(touch /tmp/bad)", "it's-safe"]);

        Assert.Equal(
            "'/Users/Test User/.dotnet/dotnet' 'workload' 'install' 'value with spaces' '$(touch /tmp/bad)' 'it'\"'\"'s-safe'",
            command);
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, false, true, true, false)]
    public void ShouldUseMacOsAdministratorPrompt_RequiresMacStructuredHostOutsideCi(
        bool isMac,
        bool ci,
        bool structuredOutput,
        bool allowElevationPrompt,
        bool expected)
    {
        Assert.Equal(expected, Util.ShouldUseMacOsAdministratorPrompt(isMac, ci, structuredOutput, allowElevationPrompt));
    }

    [Theory]
    [InlineData("execution error: User canceled. (-128)")]
    [InlineData("Execution error: User cancelled. (-128)")]
    public void WasDeclined_RecognizesMacOsAuthorizationCancellation(string error)
    {
        var result = new ShellProcessRunner.ShellProcessResult([], [error], 1);

        Assert.True(MacOsAdministratorCommandRunner.WasDeclined(result));
    }

    [Fact]
    public void WasDeclined_DoesNotHideOtherFailures()
    {
        var result = new ShellProcessRunner.ShellProcessResult([], ["xcodebuild failed"], 1);

        Assert.False(MacOsAdministratorCommandRunner.WasDeclined(result));
    }
}
