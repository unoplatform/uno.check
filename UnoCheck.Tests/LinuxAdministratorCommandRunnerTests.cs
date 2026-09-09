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
            [LinuxAdministratorCommandRunner.KeepCwdOption, "/home/test user/.dotnet/dotnet", "workload", "install", "value with spaces", "$(touch /tmp/bad)", "it's-safe"],
            arguments);
    }

    [Fact]
    public void BuildArguments_RequiresAnExecutable()
    {
        Assert.Throws<ArgumentException>(() => LinuxAdministratorCommandRunner.BuildArguments(" ", []));
    }

    [Fact]
    public void BuildArguments_KeepsTheCallersWorkingDirectory()
    {
        // pkexec switches to the target user's home unless told otherwise, which loses the
        // temporary global.json that selects which SDK a workload fix operates on.
        var arguments = LinuxAdministratorCommandRunner.BuildArguments("/usr/bin/dotnet", ["workload", "repair"]);

        Assert.Equal(LinuxAdministratorCommandRunner.KeepCwdOption, arguments[0]);
        Assert.Equal("/usr/bin/dotnet", arguments[1]);
    }

    [Fact]
    public void BuildArguments_KeepCwdPrecedesTheExecutable()
    {
        // pkexec reads its own options only before the program name; passing it afterwards
        // would hand "--keep-cwd" to the program instead.
        var arguments = LinuxAdministratorCommandRunner.BuildArguments("/bin/sh", ["-c", "apt-get install -y git"]);

        Assert.Equal(
            [LinuxAdministratorCommandRunner.KeepCwdOption, "/bin/sh", "-c", "apt-get install -y git"],
            arguments);
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
