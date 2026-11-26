using DotNetCheck;
using DotNetCheck.Cli;

namespace UnoCheck.Tests;

/// <summary>
/// The run context lets telemetry tell a host's background check from a developer's own run
/// (uno.check#643). IdeOption, Targets and the flags only take values from fixed lists, and a caller
/// that is not a short, safe name is recorded as 'other', never verbatim.
/// </summary>
public class CheckRunTelemetryContextTests
{
    static string? NoEnvironment(string name) => null;

    static Func<string, string?> EnvironmentWith(string? caller)
        => name => name == CheckRunTelemetryContext.CallerEnvironmentVariable ? caller : null;

    [Fact]
    public void A_Plain_Run_Names_No_Caller_And_Checks_Everything()
    {
        // Arrange
        var settings = new CheckSettings();

        // Act
        var context = CheckRunTelemetryContext.From(settings, NoEnvironment);

        // Assert
        Assert.Equal(new CheckRunTelemetryContext("none", "none", "all", "false", "false", "false", "false"), context);
    }

    [Fact]
    public void The_Caller_Option_Is_Recorded_Lowercased()
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings { Caller = " My-Extension " }, NoEnvironment);

        Assert.Equal("my-extension", context.Caller);
    }

    [Fact]
    public void The_Environment_Variable_Is_The_Fallback()
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings(), EnvironmentWith("uno-studio"));

        Assert.Equal("uno-studio", context.Caller);
    }

    [Fact]
    public void The_Option_Wins_Over_The_Environment_Variable()
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings { Caller = "vscode-extension" }, EnvironmentWith("uno-studio"));

        Assert.Equal("vscode-extension", context.Caller);
    }

    [Theory]
    [InlineData("C:\\Users\\jane\\tool")]
    [InlineData("jane@example.com")]
    [InlineData("two words")]
    [InlineData("-leading-dash")]
    [InlineData("a-name-that-is-much-longer-than-32-chars")]
    public void An_Unsafe_Caller_Is_Recorded_As_Other_Never_Verbatim(string caller)
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings { Caller = caller }, NoEnvironment);

        Assert.Equal("other", context.Caller);
    }

    [Fact]
    public void A_Caller_Of_Exactly_The_Maximum_Length_Is_Kept()
    {
        var caller = new string('a', CheckRunTelemetryContext.MaxCallerLength);

        Assert.Equal(caller, CheckRunTelemetryContext.NormalizeCaller(caller));
    }

    [Theory]
    [InlineData("uno-studio", true)]
    [InlineData("none", false)]
    [InlineData("other", false)]
    [InlineData("unknown", false)]
    public void Only_A_Caller_That_Names_A_Host_Is_Replayable(string caller, bool expected)
    {
        Assert.Equal(expected, CheckRunTelemetryContext.IsReplayable(caller));
    }

    [Theory]
    [InlineData("VS", "vs")]
    [InlineData(" vs ", "vs")]
    [InlineData("vscode", "vscode")]
    [InlineData("Rider", "rider")]
    [InlineData("notepad", "other")]
    [InlineData(null, "none")]
    [InlineData("  ", "none")]
    public void The_Ide_Option_Is_Recorded_From_The_Known_List(string? ide, string expected)
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings { Ide = ide }, NoEnvironment);

        Assert.Equal(expected, context.IdeOption);
    }

    [Fact]
    public void Targets_Are_Canonical_Deduplicated_And_Sorted()
    {
        // 'wasm' and 'web' are both WebAssembly; 'linux' and 'desktop' are both the Skia desktop head.
        var targets = CheckRunTelemetryContext.DescribeTargets(["wasm", "Android", "web", "linux", "desktop"]);

        Assert.Equal("android,skiadesktop,webassembly", targets);
    }

    [Fact]
    public void An_Unknown_Target_Is_Named_As_Unknown_Never_Verbatim()
    {
        var targets = CheckRunTelemetryContext.DescribeTargets(["ios", "my-secret-platform"]);

        Assert.Equal("ios,unknown", targets);
    }

    [Fact]
    public void No_Targets_Or_Any_All_Means_Every_Platform()
    {
        Assert.Equal("all", CheckRunTelemetryContext.DescribeTargets(["all"]));
        Assert.Equal("all", CheckRunTelemetryContext.DescribeTargets(["all", "ios"]));
        Assert.Equal("all", CheckRunTelemetryContext.DescribeTargets([]));
        Assert.Equal("all", CheckRunTelemetryContext.DescribeTargets(null));
    }

    [Theory]
    [InlineData(false, false, null, false, false, "false", "false", "false", "false")]
    [InlineData(true, false, null, false, false, "true", "false", "false", "false")]
    [InlineData(true, true, null, false, false, "true", "true", "false", "false")]
    [InlineData(true, false, "events.jsonl", false, false, "true", "true", "false", "false")]
    [InlineData(false, false, null, true, false, "false", "false", "true", "false")]
    [InlineData(true, false, null, false, true, "true", "false", "false", "true")]
    public void Each_Option_Is_Its_Own_Flag(
        bool nonInteractive, bool json, string? jsonFile, bool fix, bool ci,
        string expectedNonInteractive, string expectedStructuredOutput, string expectedFix, string expectedCi)
    {
        var settings = new CheckSettings { NonInteractive = nonInteractive, Json = json, JsonFile = jsonFile, Fix = fix, CI = ci };

        var context = CheckRunTelemetryContext.From(settings, NoEnvironment);

        Assert.Equal(expectedNonInteractive, context.NonInteractive);
        Assert.Equal(expectedStructuredOutput, context.StructuredOutput);
        Assert.Equal(expectedFix, context.Fix);
        Assert.Equal(expectedCi, context.CIOption);
    }

    [Fact]
    public void Missing_Settings_Fall_Back_To_The_Unknown_Context()
    {
        Assert.Same(CheckRunTelemetryContext.Unknown, CheckRunTelemetryContext.From(null!, NoEnvironment));
    }

    [Fact]
    public void A_Failing_Environment_Read_Never_Stops_The_Run()
    {
        var context = CheckRunTelemetryContext.From(new CheckSettings(), _ => throw new InvalidOperationException());

        Assert.Same(CheckRunTelemetryContext.Unknown, context);
    }

    [Fact]
    public void The_Unknown_Context_Says_Unknown_Everywhere()
    {
        Assert.All(CheckRunTelemetryContext.Unknown.ToProperties(), p => Assert.Equal("unknown", p.Value));
    }

    [Fact]
    public void Every_Event_Carries_Its_Own_Properties_Then_The_Context()
    {
        var context = new CheckRunTelemetryContext("uno-studio", "none", "all", "true", "true", "false", "false");

        var properties = TelemetryClient.BuildProperties(context, ("ReportedChecks", "~openjdk"));

        Assert.Equal(
            [
                ("ReportedChecks", "~openjdk"),
                ("Caller", "uno-studio"),
                ("IdeOption", "none"),
                ("Targets", "all"),
                ("NonInteractive", "true"),
                ("StructuredOutput", "true"),
                ("Fix", "false"),
                ("CIOption", "false"),
            ],
            properties);
    }

    [Fact]
    public void An_Event_Property_Is_Never_Repeated_By_The_Context()
    {
        // The library turns the list into a dictionary: a repeated key would throw and drop the event.
        var context = new CheckRunTelemetryContext("uno-studio", "none", "all", "true", "true", "false", "false");

        var properties = TelemetryClient.BuildProperties(context, ("Targets", "from-the-event"));

        Assert.Single(properties, p => p.Key == "Targets");
        Assert.Equal("from-the-event", properties.Single(p => p.Key == "Targets").Value);
    }

    [Fact]
    public void An_Event_Without_A_Context_Carries_The_Unknown_One()
    {
        var properties = TelemetryClient.BuildProperties(null!);

        Assert.Equal(CheckRunTelemetryContext.Unknown.ToProperties(), properties);
    }
}

/// <summary>
/// The context is built from settled settings, so it describes the run that happens: what --json,
/// --json-file, --ci and --tfm imply is applied once, before telemetry reads it.
/// </summary>
public class SettleRunTests
{
    static string? NoEnvironment(string name) => null;

    [Fact]
    public void Structured_Output_Makes_The_Run_Non_Interactive()
    {
        var settings = new CheckSettings { Json = true };

        var context = CheckCommand.SettleRun(settings, NoEnvironment);

        Assert.True(settings.NonInteractive);
        Assert.Equal("true", context.NonInteractive);
        Assert.Equal("true", context.StructuredOutput);
    }

    [Fact]
    public void A_Json_File_Makes_The_Run_Non_Interactive()
    {
        var context = CheckCommand.SettleRun(new CheckSettings { JsonFile = "events.jsonl" }, NoEnvironment);

        Assert.Equal("true", context.NonInteractive);
        Assert.Equal("true", context.StructuredOutput);
    }

    [Fact]
    public void Ci_Makes_The_Run_Non_Interactive()
    {
        var context = CheckCommand.SettleRun(new CheckSettings { CI = true }, NoEnvironment);

        Assert.Equal("true", context.NonInteractive);
        Assert.Equal("true", context.CIOption);
    }

    [Fact]
    public void Frameworks_Decide_The_Targets_And_Override_Target()
    {
        // --tfm overrides --target: this run checks Android only, and telemetry must say so.
        var settings = new CheckSettings { TargetPlatforms = ["ios"], Frameworks = ["net10.0-android"] };

        var context = CheckCommand.SettleRun(settings, NoEnvironment);

        Assert.Equal(["android"], settings.TargetPlatforms);
        Assert.Equal("android", context.Targets);
    }

    [Fact]
    public void A_Framework_With_No_Platform_Checks_Every_Platform()
    {
        var context = CheckCommand.SettleRun(new CheckSettings { TargetPlatforms = ["ios"], Frameworks = ["net10.0"] }, NoEnvironment);

        Assert.Equal("all", context.Targets);
    }

    [Fact]
    public void A_Caller_From_The_Environment_Reaches_The_Fix_Arguments()
    {
        // The case an elevated fix would otherwise lose: the host names itself only through the environment.
        var settings = new CheckSettings { Json = true };

        var context = CheckCommand.SettleRun(settings, name => name == CheckRunTelemetryContext.CallerEnvironmentVariable ? "uno-studio" : null);
        var args = CheckCommand.BuildFixArguments("openjdk", settings);

        Assert.Equal("uno-studio", context.Caller);
        Assert.Equal("uno-studio", args[Array.IndexOf(args, "--caller") + 1]);
    }

    [Fact]
    public void A_Caller_Recorded_As_Other_Is_Not_Passed_On()
    {
        var settings = new CheckSettings { Caller = "C:\\Users\\jane" };

        var context = CheckCommand.SettleRun(settings, NoEnvironment);

        Assert.Equal("other", context.Caller);
        Assert.Null(settings.Caller);
        Assert.DoesNotContain("--caller", CheckCommand.BuildFixArguments("openjdk", settings));
    }

    [Fact]
    public void A_Host_Background_Run_Reads_As_Such()
    {
        // The shape that motivated the issue: a host checking every platform, unattended, fixing nothing.
        var settings = new CheckSettings { Json = true, Caller = "uno-studio" };

        var context = CheckCommand.SettleRun(settings, NoEnvironment);

        Assert.Equal(new CheckRunTelemetryContext("uno-studio", "none", "all", "true", "true", "false", "false"), context);
    }
}

/// <summary>The run and its telemetry read one IDE list, so they cannot disagree about an --ide value.</summary>
public class KnownIdesTests
{
    [Fact]
    public void Ide_Skips_Ignore_Case_And_Surrounding_Spaces()
    {
        Assert.Equal(Util.VSSkips, KnownIdes.SkipsFor(" VS "));
    }

    [Fact]
    public void An_Unknown_Ide_Skips_Nothing()
    {
        Assert.Empty(KnownIdes.SkipsFor("notepad"));
    }

    [Theory]
    [InlineData("rider")]
    [InlineData("vs")]
    [InlineData("vscode")]
    public void Every_Ide_The_Run_Acts_On_Is_Recorded_By_Name(string ide)
    {
        Assert.Equal(ide, CheckRunTelemetryContext.DescribeIde(ide));
    }
}
