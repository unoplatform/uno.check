using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

/// <summary>Tools are started from where they are installed; the bare name, which walks the PATH, is the last resort.</summary>
public class ToolPathsTests
{
    static readonly string Root = Path.Join(Path.GetTempPath(), "uno-check-tests", "tools");
    static readonly string ProgramFiles = Path.Join(Root, "Program Files");
    static readonly string ProgramFilesX86 = Path.Join(Root, "Program Files (x86)");
    static readonly string LocalAppData = Path.Join(Root, "Users", "someone", "AppData", "Local");
    static readonly string MachineRegistryGit = Path.Join(Root, "MachineRegistry", "Git");
    static readonly string UserRegistryGit = Path.Join(Root, "UserRegistry", "Git");

    static string GitExe(string installRoot) => Path.Join(installRoot, "cmd", "git.exe");

    static string PathWith(params string[] installRoots)
        => string.Join(Path.PathSeparator, installRoots.Select(root => Path.Join(root, "cmd")).Prepend(Path.Join(Root, "node")));

    [Fact]
    public void ResolvePowerShell_InTheSystemDirectory_ReturnsItsFullPath()
    {
        var system = Path.Join(Root, "System32");
        var expected = Path.Join(system, "WindowsPowerShell", "v1.0", "powershell.exe");

        Assert.Equal(expected, ToolPaths.ResolvePowerShell(system, path => path == expected));
    }

    [Fact]
    public void ResolvePowerShell_NotInTheSystemDirectory_FallsBackToTheBareName()
        => Assert.Equal("powershell", ToolPaths.ResolvePowerShell(Path.Join(Root, "System32"), _ => false));

    [Fact]
    public void ResolvePowerShell_NoSystemDirectory_FallsBackToTheBareName()
        => Assert.Equal("powershell", ToolPaths.ResolvePowerShell(string.Empty, _ => true));

    [Fact]
    public void PowerShellCommand_SkipsTheUsersProfileAndNeverPrompts()
        => Assert.Equal("-NoProfile -NonInteractive -Command Get-ExecutionPolicy", ToolPaths.PowerShellCommand("Get-ExecutionPolicy"));

    [Fact]
    public void ExecutionPolicyCommand_IsModuleQualified_OnWindowsPowerShellsOwnModules()
        => Assert.StartsWith(ToolPaths.PowerShellOwnModulesPrefix + @"Microsoft.PowerShell.Security\Set-ExecutionPolicy ", PSExecutionPolicySolution.SetPolicyCommand);

    [Fact]
    public void GitInstallRoots_MachineLocationsComeBeforeTheUsersOwn()
    {
        var roots = ToolPaths.GitInstallRoots([MachineRegistryGit], ProgramFiles, ProgramFilesX86, [UserRegistryGit], LocalAppData, elevated: false);

        Assert.Equal(
            [MachineRegistryGit, Path.Join(ProgramFiles, "Git"), Path.Join(ProgramFilesX86, "Git"), UserRegistryGit, Path.Join(LocalAppData, "Programs", "Git")],
            roots);
    }

    [Fact]
    public void GitInstallRoots_Elevated_LeavesOutWhatTheUserCanWrite()
    {
        var roots = ToolPaths.GitInstallRoots([MachineRegistryGit], ProgramFiles, ProgramFilesX86, [UserRegistryGit], LocalAppData, elevated: true);

        Assert.Equal([MachineRegistryGit, Path.Join(ProgramFiles, "Git"), Path.Join(ProgramFilesX86, "Git")], roots);
    }

    [Fact]
    public void GitInstallRoots_MissingSpecialFolder_GivesNoRelativeRoot()
    {
        var roots = ToolPaths.GitInstallRoots([], ProgramFiles, string.Empty, [], string.Empty, elevated: false);

        Assert.Equal([Path.Join(ProgramFiles, "Git"), null, null], roots);
    }

    [Fact]
    public void ResolveGit_TakesTheFirstInstallOnThePathThatHoldsGit()
    {
        var first = Path.Join(ProgramFiles, "Git");
        var second = UserRegistryGit;

        var resolved = ToolPaths.ResolveGit([MachineRegistryGit, first, second], path => path == GitExe(first) || path == GitExe(second), PathWith(first, second));

        Assert.Equal(GitExe(first), resolved);
    }

    [Fact]
    public void ResolveGit_InstallNotOnThePath_FallsBackToTheBareName()
    {
        var install = Path.Join(ProgramFiles, "Git");

        var resolved = ToolPaths.ResolveGit([install], path => path == GitExe(install), PathWith(MachineRegistryGit));

        Assert.Equal("git", resolved);
    }

    [Fact]
    public void ResolveGit_SkipsRootsThatAreNotLocalFullPaths()
    {
        var install = Path.Join(ProgramFiles, "Git");
        var probed = new List<string>();

        var resolved = ToolPaths.ResolveGit([null!, "", "  ", "Git", @"\\server\share\Git", install], path =>
        {
            probed.Add(path);
            return true;
        }, PathWith("Git", install) + Path.PathSeparator + @"\\server\share\Git\cmd");

        Assert.Equal(GitExe(install), resolved);
        Assert.Equal([GitExe(install)], probed);
    }

    [Fact]
    public void ResolveGit_NoKnownInstall_FallsBackToTheBareName()
        => Assert.Equal("git", ToolPaths.ResolveGit([Path.Join(ProgramFiles, "Git")], _ => false, PathWith(Path.Join(ProgramFiles, "Git"))));

    [Fact]
    public void IsOnPath_IgnoresCaseQuotesAndTrailingSeparators()
    {
        var folder = Path.Join(ProgramFiles, "Git", "cmd");
        var pathValue = string.Join(Path.PathSeparator, Path.Join(Root, "node"), "\"" + folder.ToUpperInvariant() + Path.DirectorySeparatorChar + "\"");

        Assert.True(ToolPaths.IsOnPath(folder, pathValue));
    }

    [Fact]
    public void IsOnPath_FolderNotListed_IsNotOnPath()
        => Assert.False(ToolPaths.IsOnPath(Path.Join(ProgramFiles, "Git", "cmd"), Path.Join(ProgramFiles, "Git")));

    [Fact]
    public void IsOnPath_NoPath_IsNotOnPath()
        => Assert.False(ToolPaths.IsOnPath(Path.Join(ProgramFiles, "Git", "cmd"), null));
}
