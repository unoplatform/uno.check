using DotNetCheck;

namespace UnoCheck.Tests;

public class LinuxAdministratorCommandRunnerTests
{
    [Fact]
    public void BuildArguments_PreservesArgumentBoundariesVerbatim()
    {
        var arguments = LinuxAdministratorCommandRunner.BuildArguments(
            "/home/test user/.dotnet/dotnet",
            ["workload", "install", "value with spaces", "$(touch /tmp/bad)", "it's-safe"]);

        Assert.Equal(
            ["/home/test user/.dotnet/dotnet", "workload", "install", "value with spaces", "$(touch /tmp/bad)", "it's-safe"],
            arguments);
    }

    [Fact]
    public void BuildArguments_RequiresAnExecutable()
    {
        Assert.Throws<ArgumentException>(() => LinuxAdministratorCommandRunner.BuildArguments(" ", []));
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, false, true, false, false)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, false, true, false)]
    [InlineData(false, false, true, true, false)]
    public void ShouldUseLinuxAdministratorPrompt_RequiresLinuxStructuredHostOutsideCi(
        bool isLinux,
        bool ci,
        bool structuredOutput,
        bool allowElevationPrompt,
        bool expected)
    {
        Assert.Equal(expected, Util.ShouldUseLinuxAdministratorPrompt(isLinux, ci, structuredOutput, allowElevationPrompt));
    }

    [Fact]
    public void WasDeclined_RecognizesDismissedExitCode()
    {
        var result = new ShellProcessRunner.ShellProcessResult([], [], 126);

        Assert.True(LinuxAdministratorCommandRunner.WasDeclined(result));
    }

    [Fact]
    public void WasDeclined_RecognizesDismissedMessage()
    {
        var result = new ShellProcessRunner.ShellProcessResult(
            [],
            ["Error executing command as another user: Request dismissed"],
            127);

        Assert.True(LinuxAdministratorCommandRunner.WasDeclined(result));
    }

    [Fact]
    public void WasDeclined_DoesNotHideOtherFailures()
    {
        var result = new ShellProcessRunner.ShellProcessResult([], ["dotnet workload install failed"], 1);

        Assert.False(LinuxAdministratorCommandRunner.WasDeclined(result));
    }

    [Fact]
    public void WasDeclined_IgnoresSuccessfulRuns()
    {
        var result = new ShellProcessRunner.ShellProcessResult([], [], 0);

        Assert.False(LinuxAdministratorCommandRunner.WasDeclined(result));
    }
}
