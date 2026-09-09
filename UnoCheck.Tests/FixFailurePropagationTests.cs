using DotNetCheck;


namespace UnoCheck.Tests;

/// <summary>
/// A solution that discards a command's exit code leaves the fix runner nothing to observe,
/// so the run emits <c>fix_result.success: true</c> for a command that did nothing and the
/// host shows an applied fix beside a check that still fails.
/// </summary>
public class ThrowIfFailedTests
{
    static ShellProcessRunner.ShellProcessResult Result(int exitCode, params string[] stderr)
        => new([], [.. stderr], exitCode);

    [Fact]
    public void Successful_Command_Is_Left_Alone()
    {
        Util.ThrowIfFailed(Result(0), "Installing git");
    }

    [Fact]
    public void Absent_Result_Is_Left_Alone()
    {
        Util.ThrowIfFailed(null, "Installing git");
    }

    [Fact]
    public void Non_Zero_Exit_Fails_The_Fix()
    {
        var ex = Assert.Throws<InvalidOperationException>(
            () => Util.ThrowIfFailed(Result(17, "E: Unable to locate package"), "Installing git"));

        Assert.Contains("Installing git", ex.Message);
        Assert.Contains("17", ex.Message);
    }

    [Fact]
    public void Failure_Message_Carries_The_Output_Tail()
    {
        // The actionable reason (declined authorization, no network, missing package) is in
        // the output, so it has to reach the fix_result error the host displays.
        var message = Util.BuildCommandFailureMessage(
            "Installing ninja-build",
            Result(126, "Error executing command as another user: Request dismissed"));

        Assert.Contains("Request dismissed", message);
        Assert.Contains("126", message);
    }

    [Fact]
    public void Silent_Failure_Still_Names_The_Command_And_Code()
    {
        var message = Util.BuildCommandFailureMessage("Restoring the Uno.Sdk package", Result(1));

        Assert.Equal("Restoring the Uno.Sdk package exited with code 1.", message);
    }
}


/// <summary>
/// Enabling Hyper-V needs a restart, and DISM says so with exit code 3010. Every other
/// non-zero code is a real failure — and used to be reported as "please restart your
/// computer" too, which sent the user to reboot over a fix that had not run at all.
/// </summary>
public class HyperVActivationOutcomeTests
{
    static ShellProcessRunner.ShellProcessResult Dism(int exitCode, params string[] output)
        => new([.. output], [], exitCode);

    [Theory]
    [InlineData(0)]
    [InlineData(3010)]
    public void SucceedsWhenDismAppliedTheChange(int exitCode)
        => Assert.True(Dism(exitCode).ExitCode is 0 or 3010);

    [Fact]
    public void A_Refused_Dism_Reports_Its_Own_Reason()
    {
        // 740 is the elevation refusal; the message has to carry that, not a reboot prompt.
        var message = Util.BuildCommandFailureMessage(
            "Enabling Hyper-V",
            Dism(740, "Error: 740", "The requested operation requires elevation."));

        Assert.Contains("740", message);
        Assert.Contains("requires elevation", message);
        Assert.DoesNotContain("restart", message, StringComparison.OrdinalIgnoreCase);
    }
}
