using DotNetCheck;

namespace UnoCheck.Tests;

/// <summary>
/// A tool started by bare name makes Windows search every PATH directory, and in a sandboxed host each of those
/// reads asks the user (issue #646). Tools are started from where they are installed; the bare name is the last resort.
/// </summary>
public class WindowsToolPathsTests
{
    static readonly string Root = Path.Join(Path.GetTempPath(), "uno-check-tests", "tools");

    [Fact]
    public void ResolvePowerShell_InTheSystemDirectory_ReturnsItsFullPath()
    {
        var system = Path.Join(Root, "System32");
        var expected = Path.Join(system, "WindowsPowerShell", "v1.0", "powershell.exe");

        var resolved = WindowsToolPaths.ResolvePowerShell(system, path => path == expected);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolvePowerShell_NotInTheSystemDirectory_FallsBackToTheBareName()
    {
        var resolved = WindowsToolPaths.ResolvePowerShell(Path.Join(Root, "System32"), _ => false);

        Assert.Equal("powershell", resolved);
    }

    [Fact]
    public void ResolveGit_TakesTheFirstInstallRootThatHoldsGit()
    {
        var registry = Path.Join(Root, "FromRegistry", "Git");
        var programFiles = Path.Join(Root, "Program Files", "Git");
        var expected = Path.Join(programFiles, "cmd", "git.exe");

        var resolved = WindowsToolPaths.ResolveGit([registry, programFiles], path => path == expected);

        Assert.Equal(expected, resolved);
    }

    [Fact]
    public void ResolveGit_SkipsEmptyRoots()
    {
        var programFiles = Path.Join(Root, "Program Files", "Git");
        var expected = Path.Join(programFiles, "cmd", "git.exe");
        var probed = new List<string>();

        var resolved = WindowsToolPaths.ResolveGit([null!, "", "  ", programFiles], path =>
        {
            probed.Add(path);
            return path == expected;
        });

        Assert.Equal(expected, resolved);
        Assert.Equal([expected], probed);
    }

    [Fact]
    public void ResolveGit_NoKnownInstall_FallsBackToTheBareName()
    {
        var resolved = WindowsToolPaths.ResolveGit([Path.Join(Root, "Program Files", "Git")], _ => false);

        Assert.Equal("git", resolved);
    }

    [Fact]
    public void Git_OffWindows_IsTheBareName()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal("git", WindowsToolPaths.Git());
    }

    [Fact]
    public void PowerShell_OffWindows_IsTheBareName()
    {
        if (OperatingSystem.IsWindows())
            return;

        Assert.Equal("powershell", WindowsToolPaths.PowerShell());
    }
}
