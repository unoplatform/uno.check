using DotNetCheck;
using DotNetCheck.Cli;
using DotNetCheck.Models;

namespace UnoCheck.Tests;

/// <summary>
/// The fix argument vector is the contract's only instruction for how a host re-invokes the
/// tool, often elevated. It has to reproduce the diagnosis that produced it: a run diagnosed
/// on the preview channel whose fix arguments omitted the channel came back healthy against
/// stable without fixing anything.
/// </summary>
public class BuildFixArgumentsTests
{
    static string[] Build(string id, CheckSettings settings)
        => CheckCommand.BuildFixArguments(id, settings);

    [Fact]
    public void Always_Names_The_Checkup_And_Runs_Unattended()
    {
        Assert.Equal(
            ["--fix", "--only", "dotnet", "--non-interactive"],
            Build("dotnet", new CheckSettings()));
    }

    [Fact]
    public void Missing_Settings_Still_Produce_A_Runnable_Vector()
    {
        Assert.Equal(
            ["--fix", "--only", "dotnet", "--non-interactive"],
            Build("dotnet", null!));
    }

    [Fact]
    public void Preview_Major_Channel_Survives()
    {
        // The reported repro: '--json --preview-major --only dotnet' found .NET missing, then
        // its fix arguments re-ran against stable and reported healthy without fixing anything.
        var args = Build("dotnet", new CheckSettings { PreviewMajor = true });

        Assert.Contains("--preview-major", args);
    }

    [Fact]
    public void Preview_And_Dev_Manifest_Channels_Survive()
    {
        var args = Build("dotnet", new CheckSettings { Preview = true, Main = true });

        Assert.Contains("--pre", args);
        Assert.Contains("--dev-manifest", args);
    }

    [Fact]
    public void Manifest_And_Sdk_Root_Travel_As_Separate_Elements()
    {
        var args = Build("dotnet", new CheckSettings
        {
            Manifest = @"C:\my manifest\uno.check.manifest.json",
            DotNetSdkRoot = @"C:\Program Files\dotnet",
        });

        // A vector, never a joined command string: the space-bearing values stay one element
        // each so no host quoting is required or possible to get wrong.
        Assert.Equal(@"C:\my manifest\uno.check.manifest.json", args[Array.IndexOf(args, "--manifest") + 1]);
        Assert.Equal(@"C:\Program Files\dotnet", args[Array.IndexOf(args, "--dotnet") + 1]);
    }

    [Fact]
    public void Force_Dotnet_And_Ci_Survive()
    {
        var args = Build("dotnet", new CheckSettings { ForceDotNet = true, CI = true });

        Assert.Contains("--force-dotnet", args);
        Assert.Contains("--ci", args);
    }

    [Fact]
    public void Target_Platforms_Are_Replayed_When_No_Framework_Was_Given()
    {
        var args = Build("openjdk", new CheckSettings { TargetPlatforms = ["android", "ios"] });

        Assert.Equal(
            ["--fix", "--only", "openjdk", "--non-interactive", "--target", "android", "--target", "ios"],
            args);
    }

    [Fact]
    public void Frameworks_Replace_The_Platforms_They_Derived()
    {
        // --tfm derives the target platforms during the run, so replaying the frameworks
        // reproduces that derivation. Emitting both would re-state the same selection twice.
        var args = Build("openjdk", new CheckSettings
        {
            Frameworks = ["net9.0-android"],
            TargetPlatforms = ["android"],
        });

        Assert.Contains("--tfm", args);
        Assert.Equal("net9.0-android", args[Array.IndexOf(args, "--tfm") + 1]);
        Assert.DoesNotContain("--target", args);
    }

    [Fact]
    public void Skips_Ide_And_Sdk_Version_Survive()
    {
        var args = Build("openjdk", new CheckSettings
        {
            Skip = ["git"],
            Ide = "vscode",
            UnoSdkVersion = "5.5.0",
        });

        Assert.Equal("git", args[Array.IndexOf(args, "--skip") + 1]);
        Assert.Equal("vscode", args[Array.IndexOf(args, "--ide") + 1]);
        Assert.Equal("5.5.0", args[Array.IndexOf(args, "--unoSdkVersion") + 1]);
    }

    [Fact]
    public void Output_Options_Belong_To_The_Host_And_Are_Never_Replayed()
    {
        var args = Build("dotnet", new CheckSettings
        {
            Json = true,
            JsonFile = "out.jsonl",
            CorrelationId = "abc",
            Verbose = true,
            LogFile = "run.log",
        });

        Assert.DoesNotContain("--json", args);
        Assert.DoesNotContain("--json-file", args);
        Assert.DoesNotContain("--correlation-id", args);
        Assert.DoesNotContain("--verbose", args);
        Assert.DoesNotContain("--logfile", args);
    }
}

public class HealthCheckFixArgumentTests
{
    class FixableCheckup : Checkup
    {
        public override string Id => "dotnet";
        public override string Title => "Fake dotnet";
        public override Task<DiagnosticResult> Examine(SharedState history)
            => Task.FromResult(DiagnosticResult.Ok(this));
    }

    class NoopSolution : Solution
    {
    }

    [Fact]
    public void Fix_Arguments_Carry_The_Settings_The_Diagnosis_Ran_With()
    {
        var checkup = new FixableCheckup();
        var diagnosis = new DiagnosticResult(
            Status.Error,
            checkup,
            "missing",
            new Suggestion("Install", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(
            checkup,
            diagnosis,
            new CheckSettings { PreviewMajor = true, DotNetSdkRoot = @"C:\Program Files\dotnet" });

        Assert.NotNull(check.Fix);
        Assert.Contains("--preview-major", check.Fix!.Args!);
        Assert.Contains(@"C:\Program Files\dotnet", check.Fix.Args!);
    }
}
