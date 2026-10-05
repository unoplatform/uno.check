using DotNetCheck.Checkups;
using NuGet.Versioning;

namespace UnoCheck.Tests;

/// <summary>
/// The Xcode checkup printed the required version twice ("Xcode.app (26.5 26.5) not installed.") because the
/// manifests no longer name the Xcode build, so the name fell back to the version itself. It also said
/// "not installed" when an older, incompatible Xcode was installed, which sent testers looking for the wrong problem.
/// </summary>
public class XCodeCheckupTests
{
    [Fact]
    public void RequiredVersionIsShownOnceWithoutABuildName()
    {
        Assert.Equal("26.5", XCodeCheckup.FormatRequiredVersion(NuGetVersion.Parse("26.5"), null));
    }

    [Fact]
    public void RequiredVersionIsShownOnceWhenTheNameIsTheVersion()
    {
        Assert.Equal("26.5", XCodeCheckup.FormatRequiredVersion(NuGetVersion.Parse("26.5"), "26.5"));
    }

    [Fact]
    public void RequiredVersionIncludesTheBuildNameWhenTheManifestHasOne()
    {
        Assert.Equal("16.0 (15F31d)", XCodeCheckup.FormatRequiredVersion(NuGetVersion.Parse("16.0"), "15F31d"));
    }

    [Theory]
    [InlineData(true, "26.5")]
    [InlineData(false, "26.5 or newer")]
    public void RequirementSaysWhetherANewerXcodeIsAccepted(bool exact, string expected)
    {
        Assert.Equal(expected, XCodeCheckup.FormatRequirement("26.5", exact));
    }

    [Fact]
    public void AnOlderInstalledXcodeIsNamedRatherThanReportedMissing()
    {
        var message = XCodeCheckup.FormatMissingMessage("26.5 or newer", new[] { "26.2 (17C52)" });

        Assert.Equal("Xcode 26.2 (17C52) is installed, but Xcode 26.5 or newer is required.", message);
        Assert.DoesNotContain("not installed", message);
    }

    [Fact]
    public void SeveralInstalledXcodesAreEachNamedOnce()
    {
        Assert.Equal(
            "Xcode 26.2, 16.4 are installed, but Xcode 26.5 is required.",
            XCodeCheckup.FormatMissingMessage("26.5", new[] { "26.2", "16.4", "26.2" }));
    }

    [Fact]
    public void NoXcodeIsReportedAsNotInstalled()
    {
        Assert.Equal(
            "Xcode is not installed. Xcode 26.5 or newer is required.",
            XCodeCheckup.FormatMissingMessage("26.5 or newer", Array.Empty<string>()));
    }
}
