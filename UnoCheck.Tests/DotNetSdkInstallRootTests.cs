using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

/// <summary>
/// The elevation answer a host receives has to describe the installation the fix will
/// actually write to: with a writable DOTNET_ROOT in the environment and a protected root
/// requested for the run, probing the environment reported no elevation needed and the host
/// launched a fix without the permissions it required.
/// </summary>
public class DotNetSdkInstallRootTests
{
    [Fact]
    public void The_Run_Requested_Root_Outranks_The_Process_Environment()
    {
        var requested = Path.Combine(Path.GetTempPath(), "uno-check-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(requested);
        try
        {
            var state = new DotNetCheck.Models.SharedState();
            state.SetEnvironmentVariable("DOTNET_ROOT", requested);

            Assert.Equal(requested, DotNetSdkScriptInstallSolution.ResolveInstallRoot(state));
        }
        finally
        {
            Directory.Delete(requested, recursive: true);
        }
    }

    [Fact]
    public void The_Probe_And_The_Install_Read_The_Same_Root()
    {
        var requested = Path.Combine(Path.GetTempPath(), "uno-check-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(requested);
        try
        {
            var state = new DotNetCheck.Models.SharedState();
            state.SetEnvironmentVariable("DOTNET_ROOT", requested);

            var solution = new DotNetSdkScriptInstallSolution("9.0.100", state);

            Assert.Equal(requested, solution.InstallRoot);
            Assert.False(solution.RequiresElevation); // a temp directory is user-writable
        }
        finally
        {
            Directory.Delete(requested, recursive: true);
        }
    }

    [Fact]
    public void An_Unusable_Requested_Root_Falls_Back_Instead_Of_Being_Used()
    {
        var state = new DotNetCheck.Models.SharedState();
        state.SetEnvironmentVariable("DOTNET_ROOT", Path.Combine(Path.GetTempPath(), "uno-check-missing-" + Guid.NewGuid().ToString("N")));

        Assert.NotEqual(state.GetEnvironmentVariable("DOTNET_ROOT"), DotNetSdkScriptInstallSolution.ResolveInstallRoot(state));
    }

    [Fact]
    public void The_Default_Root_Is_Always_Absolute()
    {
        // Path.Combine returns the child on its own when the parent is empty, which would
        // resolve the install against the working directory instead of the machine root.
        Assert.True(Path.IsPathRooted(DotNetSdkScriptInstallSolution.DefaultSdkRoot()));
    }
}
