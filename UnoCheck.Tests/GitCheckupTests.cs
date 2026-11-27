using System.Diagnostics;
using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>How the Git check runs Git: from its known install first, and without reading any git config file.</summary>
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
    public void VersionCommand_TurnsOffTheSystemAndGlobalConfig()
    {
        var options = GitCheckup.VersionCommand(GitExe);

        Assert.Equal(GitExe, options.Executable);
        Assert.Equal("--version", options.Args);
        Assert.Equal("1", options.EnvironmentVariables["GIT_CONFIG_NOSYSTEM"]);
        Assert.Equal(OperatingSystem.IsWindows() ? "NUL" : "/dev/null", options.EnvironmentVariables["GIT_CONFIG_GLOBAL"]);
    }

    // Git reads its global config even for --version: a broken one makes plain `git --version` fail.
    [Fact]
    public void VersionCommand_GitDoesNotReadABrokenGlobalConfig()
    {
        var home = Directory.CreateTempSubdirectory("uno-check-git-home-").FullName;
        try
        {
            File.WriteAllText(Path.Join(home, ".gitconfig"), "[broken");

            if (RunGitVersion(home, new Dictionary<string, string>()) is not int plainExitCode)
                return; // no git on this machine

            Assert.NotEqual(0, plainExitCode);
            Assert.Equal(0, RunGitVersion(home, GitCheckup.VersionCommand("git").EnvironmentVariables));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    static int? RunGitVersion(string home, IReadOnlyDictionary<string, string> environment)
    {
        var psi = new ProcessStartInfo("git", "--version") { RedirectStandardOutput = true, RedirectStandardError = true };
        psi.Environment["HOME"] = home;
        foreach (var variable in environment)
            psi.Environment[variable.Key] = variable.Value;

        try
        {
            using var process = Process.Start(psi)!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
