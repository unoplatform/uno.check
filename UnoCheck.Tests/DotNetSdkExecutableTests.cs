using DotNetCheck.DotNet;
using DotNetCheck.Models;

namespace UnoCheck.Tests;

/// <summary>
/// A step that runs dotnet by name depends on the host process's PATH, which a desktop host
/// started from a launcher may not have; the executable under the examined root does not.
/// </summary>
public class DotNetSdkExecutableTests
{
    [Fact]
    public void The_Executable_Under_The_Recorded_Root_Is_Used()
    {
        var root = Path.Combine(Path.GetTempPath(), "uno-check-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var executable = Path.Combine(root, DotNetSdk.DotNetExeName);
        File.WriteAllText(executable, string.Empty);
        try
        {
            var state = new SharedState();
            state.SetEnvironmentVariable("DOTNET_ROOT", root);

            Assert.Equal(executable, DotNetSdk.ResolveExecutable(state));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Without_A_Recorded_Root_The_Name_Is_Used()
    {
        Assert.Equal("dotnet", DotNetSdk.ResolveExecutable(new SharedState()));
    }

    [Fact]
    public void A_Recorded_Root_Without_The_Executable_Falls_Back_To_The_Name()
    {
        var root = Path.Combine(Path.GetTempPath(), "uno-check-root-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var state = new SharedState();
            state.SetEnvironmentVariable("DOTNET_ROOT", root);

            Assert.Equal("dotnet", DotNetSdk.ResolveExecutable(state));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
