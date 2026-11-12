using DotNetCheck.Checkups;
using DotNetCheck.Manifest;
using DotNetCheck.Models;
using NuGet.Versioning;
using ManifestSdk = DotNetCheck.Manifest.DotNetSdk;

namespace UnoCheck.Tests;

/// <summary>
/// uno-check passed Xcode 27.2 for a net10.0-ios app on the .NET 11 SDK, and the build failed with "This version of
/// .NET for iOS (26.0.11017) requires Xcode 26.0" (issue #618). The .NET 11 Apple workloads build net10.0 targets
/// with their net10.0_26.0 packs, which accept only Xcode 26.0, while the manifest's Xcode is for net11.0.
/// </summary>
public class XcodeTargetFrameworksTests
{
    const string Download = "https://developer.apple.com/download/all/";

    static readonly Dictionary<string, string> Net10NeedsXcode26 = new() { ["net10.0"] = "26.0" };

    static XCodeCheckup CheckupFor(string minimumVersion = "27.2", Dictionary<string, string>? targetFrameworks = null)
        => new()
        {
            Manifest = new Manifest
            {
                Check = new Check
                {
                    XCode = new XCodeRequirement { MinimumVersion = minimumVersion, TargetFrameworks = targetFrameworks ?? Net10NeedsXcode26 },
                    DotNet = new DotNet { Sdks = [new ManifestSdk { Version = "11.0.100" }] },
                },
            },
        };

    static XCodeCheckup ExaminedFor(params string[] targetFrameworks)
    {
        var state = new SharedState();
        if (targetFrameworks.Length > 0)
            state.ContributeState(StateKey.EntryPoint, StateKey.TargetFrameworks, targetFrameworks);

        var checkup = CheckupFor();
        checkup.ShouldExamine(state);
        return checkup;
    }

    static XCodeInfo Xcode(string version, string build, string path, bool selected = false)
        => new(NuGetVersion.Parse(version), version, build, path, selected);

    [Fact]
    public void Classify_Net10AppleTarget_NeedsTheOlderXcode()
    {
        var targets = XcodeTargetFrameworks.Classify(["net10.0-ios"], Net10NeedsXcode26);

        Assert.Empty(targets.Current);
        Assert.Equal([new OlderTarget("net10.0-ios", "net10.0", "26.0")], targets.Older);
        Assert.Equal("26.0", targets.OlderOnlyXcodeVersion);
        Assert.Empty(targets.Uncovered);
    }

    [Theory]
    [InlineData("net10.0-tvos")]
    [InlineData("net10.0-maccatalyst")]
    [InlineData("net10.0-macos")]
    [InlineData("net10.0-ios18.0")]
    [InlineData(" NET10.0-IOS ")]
    public void Classify_EveryNet10ApplePlatform_NeedsTheOlderXcode(string targetFramework)
    {
        var targets = XcodeTargetFrameworks.Classify([targetFramework], Net10NeedsXcode26);

        Assert.Equal("26.0", targets.OlderOnlyXcodeVersion);
    }

    [Fact]
    public void Classify_CurrentAppleTarget_UsesTheManifestXcode()
    {
        var targets = XcodeTargetFrameworks.Classify(["net11.0-ios", "net11.0-tvos"], Net10NeedsXcode26);

        Assert.Equal(["net11.0-ios", "net11.0-tvos"], targets.Current);
        Assert.Empty(targets.Older);
        Assert.Null(targets.OlderOnlyXcodeVersion);
        Assert.Empty(targets.Uncovered);
    }

    [Fact]
    public void Classify_CurrentAndOlderAppleTargets_LeaveTheOlderOnesUncovered()
    {
        var targets = XcodeTargetFrameworks.Classify(["net10.0-ios", "net11.0-ios"], Net10NeedsXcode26);

        Assert.Null(targets.OlderOnlyXcodeVersion);
        Assert.Equal([new OlderTarget("net10.0-ios", "net10.0", "26.0")], targets.Uncovered);
    }

    [Fact]
    public void Classify_OlderTargetsNeedingDifferentXcodes_LeaveThemAllUncovered()
    {
        var table = new Dictionary<string, string> { ["net9.0"] = "26.0", ["net10.0"] = "26.2" };

        var targets = XcodeTargetFrameworks.Classify(["net9.0-ios", "net10.0-ios"], table);

        Assert.Null(targets.OlderOnlyXcodeVersion);
        Assert.Equal(2, targets.Uncovered.Count);
    }

    [Theory]
    [InlineData("net10.0-android")]
    [InlineData("net10.0-browserwasm")]
    [InlineData("net10.0-desktop")]
    [InlineData("net10.0-windows10.0.19041")]
    [InlineData("net10.0")]
    [InlineData("")]
    [InlineData("not-a-tfm")]
    public void Classify_NonAppleTargets_AreIgnored(string targetFramework)
    {
        var targets = XcodeTargetFrameworks.Classify([targetFramework], Net10NeedsXcode26);

        Assert.Empty(targets.Current);
        Assert.Empty(targets.Older);
    }

    [Fact]
    public void Classify_WithoutATable_UsesTheManifestXcodeForEveryAppleTarget()
    {
        var targets = XcodeTargetFrameworks.Classify(["net10.0-ios"], null!);

        Assert.Equal(["net10.0-ios"], targets.Current);
        Assert.Null(targets.OlderOnlyXcodeVersion);
    }

    [Fact]
    public void Classify_UnparsableXcodeVersion_IsIgnored()
    {
        var targets = XcodeTargetFrameworks.Classify(["net10.0-ios"], new Dictionary<string, string> { ["net10.0"] = "latest" });

        Assert.Equal(["net10.0-ios"], targets.Current);
    }

    [Theory]
    [InlineData("26.0", "26.0", true)]
    [InlineData("26.0.1", "26.0", true)]
    [InlineData("26.1", "26.0", false)]
    [InlineData("27.2", "26.0", false)]
    [InlineData("25.0", "26.0", false)]
    public void MatchesMajorMinor_ComparesOnlyMajorAndMinor(string xcode, string required, bool expected)
        => Assert.Equal(expected, XcodeTargetFrameworks.MatchesMajorMinor(NuGetVersion.Parse(xcode), required));

    [Theory]
    [InlineData("net10.0-ios", "net11.0-ios")]
    [InlineData("net10.0-maccatalyst", "net11.0-maccatalyst")]
    [InlineData("net10.0-ios18.0", "net11.0-ios")]
    public void Retarget_KeepsThePlatform(string targetFramework, string expected)
        => Assert.Equal(expected, XcodeTargetFrameworks.Retarget(targetFramework, "net11.0"));

    [Fact]
    public void Checkup_Net10Target_RequiresXcode26()
    {
        var checkup = ExaminedFor("net10.0-ios", "net10.0-android");

        Assert.Equal("Required Xcode 26.0 for net10.0-ios", checkup.Title);
        Assert.Equal("26.0", checkup.RequiredVersion);
        Assert.True(checkup.IsRequiredXcode(NuGetVersion.Parse("26.0.1")));
        Assert.False(checkup.IsRequiredXcode(NuGetVersion.Parse("27.2")));
    }

    [Fact]
    public void Checkup_Net10Target_NamesTheReasonAndTheWayOut()
    {
        var result = ExaminedFor("net10.0-ios")
            .MissingXcodeResult([Xcode("27.2", "27B52", "/Applications/Xcode.app", selected: true)]);

        Assert.Equal(
            "Xcode 27.2 (27B52) is installed, but Xcode 26.0 is required. net10.0-ios builds with this .NET SDK's net10.0 Apple SDK packs, "
            + "which require Xcode 26.0. To use Xcode 27.2, move to net11.0-ios.",
            result.Message);
        Assert.Equal($"Download Xcode 26.0 from {Download}", result.Suggestion.Name);
    }

    [Theory]
    [InlineData]
    [InlineData("net11.0-ios")]
    [InlineData("net10.0-android")]
    public void Checkup_WithoutNet10AppleTargets_KeepsTheManifestXcode(params string[] targetFrameworks)
    {
        var checkup = ExaminedFor(targetFrameworks);

        Assert.Equal("Required Xcode 27.2 (newer version might not be supported)", checkup.Title);
        Assert.Equal("27.2", checkup.RequiredVersion);
        Assert.True(checkup.IsRequiredXcode(NuGetVersion.Parse("27.2")));
        Assert.False(checkup.IsRequiredXcode(NuGetVersion.Parse("26.0")));
    }

    [Fact]
    public void Checkup_RecordsWhetherTargetFrameworksWereGiven()
    {
        Assert.False(ExaminedFor().TargetFrameworksGiven);
        Assert.True(ExaminedFor("net10.0-android").TargetFrameworksGiven);
    }

    [Fact]
    public void Checkup_CurrentAndNet10Targets_ChecksTheManifestXcodeAndLeavesNet10Uncovered()
    {
        var checkup = ExaminedFor("net10.0-ios", "net11.0-ios");

        Assert.Equal("27.2", checkup.RequiredVersion);
        Assert.Equal([new OlderTarget("net10.0-ios", "net10.0", "26.0")], checkup.Targets.Uncovered);
    }

    [Fact]
    public void UncoveredTargetsWarning_NamesWhatTheTargetNeeds()
    {
        var warning = XCodeCheckup.FormatUncoveredTargetsWarning([new OlderTarget("net10.0-ios", "net10.0", "26.0")], "27.2", "net11.0");

        Assert.Equal(
            "With this .NET SDK, net10.0-ios needs Xcode 26.0, so the selected Xcode 27.2 can't build it. "
            + "Move to net11.0-ios, or select the Xcode it needs when you build it.",
            warning);
    }

    [Fact]
    public void UncoveredTargetsWarning_NamesWhatEachTargetNeeds()
    {
        var uncovered = new[] { new OlderTarget("net9.0-ios", "net9.0", "26.0"), new OlderTarget("net10.0-tvos", "net10.0", "26.2") };

        var warning = XCodeCheckup.FormatUncoveredTargetsWarning(uncovered, "27.2", null!);

        Assert.Equal(
            "With this .NET SDK, net9.0-ios needs Xcode 26.0, net10.0-tvos needs Xcode 26.2, so the selected Xcode 27.2 can't build them. "
            + "Select the Xcode each needs when you build them.",
            warning);
    }

    [Fact]
    public void OlderTargetsHint_SelectedXcodeCannotBuildNet10_ExplainsIt()
    {
        var hint = XCodeCheckup.FormatOlderTargetsHint(Net10NeedsXcode26, NuGetVersion.Parse("27.2"), "27.2", "net11.0");

        Assert.Equal(
            "With this .NET SDK, net10.0 Apple targets (iOS, tvOS, Mac Catalyst) need Xcode 26.0, and the selected Xcode 27.2 "
            + "builds net11.0 ones. Pass your app's target frameworks with --tfm to check them.",
            hint);
    }

    [Fact]
    public void OlderTargetsHint_SelectedXcodeBuildsNet10_IsNotShown()
    {
        Assert.Null(XCodeCheckup.FormatOlderTargetsHint(Net10NeedsXcode26, NuGetVersion.Parse("26.0.1"), "26.0.1", "net11.0"));
        Assert.Null(XCodeCheckup.FormatOlderTargetsHint(null!, NuGetVersion.Parse("27.2"), "27.2", "net11.0"));
    }
}
