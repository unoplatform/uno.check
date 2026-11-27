using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>Git is run from its known install first, and by bare name only when that fails.</summary>
public class GitCheckupTests
{
    static readonly string GitExe = Path.Join(Path.GetTempPath(), "uno-check-tests", "tools", "Program Files", "Git", "cmd", "git.exe");

    [Fact]
    public void RunFromKnownInstall_Succeeds_DoesNotTryTheBareName()
    {
        var tried = new List<string>();

        var succeeded = GitCheckup.RunFromKnownInstall(GitExe, command =>
        {
            tried.Add(command);
            return true;
        });

        Assert.True(succeeded);
        Assert.Equal([GitExe], tried);
    }

    [Fact]
    public void RunFromKnownInstall_KnownInstallFails_TriesTheBareName()
    {
        var tried = new List<string>();

        var succeeded = GitCheckup.RunFromKnownInstall(GitExe, command =>
        {
            tried.Add(command);
            return command == "git";
        });

        Assert.True(succeeded);
        Assert.Equal([GitExe, "git"], tried);
    }

    [Fact]
    public void RunFromKnownInstall_AlreadyTheBareName_RunsOnce()
    {
        var tried = new List<string>();

        var succeeded = GitCheckup.RunFromKnownInstall("git", command =>
        {
            tried.Add(command);
            return false;
        });

        Assert.False(succeeded);
        Assert.Equal(["git"], tried);
    }

    [Fact]
    public void VersionCommand_ReadsNoGitConfigFile()
    {
        var options = GitCheckup.VersionCommand(GitExe);

        Assert.Equal(GitExe, options.Executable);
        Assert.Equal("--version", options.Args);
        Assert.Equal("1", options.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal(OperatingSystem.IsWindows() ? "NUL" : "/dev/null", options.EnvironmentVariables["GIT_CONFIG_GLOBAL"]);
    }
}
