using DotNetCheck;
using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

/// <summary>
/// Boots runs <c>sudo installer</c> itself, which cannot prompt without a terminal. When the
/// administrator dialog is active, the package install has to go through the elevation seam
/// instead.
/// </summary>
public class BootsSolutionTests
{
    const string PkgFile = "/var/folders/My Temp/it's.pkg";

    [Fact]
    public async Task Pkg_Install_Runs_The_Installer_Through_The_Elevation_Seam()
    {
        string? command = null;
        string[] arguments = [];

        await BootsSolution.InstallPkgAsync(
            PkgFile,
            "Microsoft OpenJDK 17",
            (cmd, args, _) =>
            {
                command = cmd;
                arguments = args;
                return Task.FromResult(new ShellProcessRunner.ShellProcessResult([], [], 0));
            },
            CancellationToken.None);

        Assert.Equal("/usr/sbin/installer", command);
        Assert.Equal(["-verbose", "-dumplog", "-pkg", PkgFile, "-target", "/"], arguments);
    }

    [Fact]
    public async Task Declined_Authorization_Fails_The_Fix_With_Its_Reason()
    {
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => BootsSolution.InstallPkgAsync(
            PkgFile,
            "Microsoft OpenJDK 17",
            (_, _, _) => Task.FromResult(new ShellProcessRunner.ShellProcessResult([], ["Administrator approval was declined."], 1)),
            CancellationToken.None));

        Assert.Contains("Microsoft OpenJDK 17", ex.Message);
        Assert.Contains("Administrator approval was declined.", ex.Message);
    }
}
