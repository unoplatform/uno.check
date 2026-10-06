using Claunia.PropertyList;
using DotNetCheck.Checkups;
using DotNetCheck.Cli;
using DotNetCheck.Manifest;
using NuGet.Versioning;

namespace UnoCheck.Tests;

/// <summary>
/// The Xcode checkup printed the required version twice ("Xcode.app (26.5 26.5) not installed.") because the
/// manifests no longer name the Xcode build, so the name fell back to the version itself. It also said
/// "not installed" when an older, incompatible Xcode was installed, which sent testers looking for the wrong problem,
/// and with --json the report only carried the download suggestion, not the reason.
/// </summary>
public class XCodeCheckupTests
{
    const string Download = "https://developer.apple.com/download/all/";

    static XCodeCheckup CheckupFor(
        string? minimumVersion = null, string? minimumVersionName = null, string? exactVersion = null, string? exactVersionName = null)
        => new()
        {
            Manifest = new Manifest
            {
                Check = new Check
                {
                    XCode = new MinExactVersion
                    {
                        MinimumVersion = minimumVersion,
                        MinimumVersionName = minimumVersionName,
                        ExactVersion = exactVersion,
                        ExactVersionName = exactVersionName,
                    },
                },
            },
        };

    static XCodeInfo Xcode(string version, string build, string path, bool selected = false)
        => new(NuGetVersion.Parse(version), version, build, path, selected);

    [Fact]
    public void RequiredVersionIsShownOnceWithoutABuildName()
    {
        Assert.Equal("26.5", CheckupFor(minimumVersion: "26.5").RequiredVersion);
    }

    [Theory]
    [InlineData("26.5")]
    [InlineData("26.5.0")]
    public void RequiredVersionIsShownOnceWhenTheNameIsTheSameVersion(string name)
    {
        Assert.Equal("26.5", XCodeCheckup.FormatRequiredVersion(NuGetVersion.Parse("26.5"), name));
    }

    [Fact]
    public void RequiredVersionIncludesTheBuildNameWhenTheManifestHasOne()
    {
        Assert.Equal("16.0 (15F31d)", CheckupFor(minimumVersion: "16.0", minimumVersionName: "15F31d").RequiredVersion);
    }

    [Fact]
    public void RequiredVersionTakesTheNameFromTheSameEntryAsTheVersion()
    {
        var checkup = CheckupFor(minimumVersion: "16.0", minimumVersionName: "15F31d", exactVersion: "16.1");

        Assert.Equal("16.1", checkup.RequiredVersion);
    }

    [Fact]
    public void TitleKeepsThePlainVersion()
    {
        Assert.Equal("Required Xcode 26.5 (newer version might not be supported)", CheckupFor(minimumVersion: "26.5").Title);
        Assert.Equal(
            "Required Xcode 16.0 (newer version might not be supported)",
            CheckupFor(minimumVersion: "16.0", minimumVersionName: "15F31d").Title);
    }

    [Fact]
    public void AnOlderInstalledXcodeIsNamedRatherThanReportedMissing()
    {
        var result = CheckupFor(minimumVersion: "26.5")
            .MissingXcodeResult(new[] { Xcode("26.2", "17C52", "/Applications/Xcode.app", selected: true) });

        Assert.Equal("Xcode 26.2 (17C52) is installed, but Xcode 26.5 is required.", result.Message);
        Assert.Equal($"Download Xcode 26.5 from {Download}", result.Suggestion.Name);
    }

    [Fact]
    public void TheJsonReportCarriesTheReasonNotOnlyTheSuggestion()
    {
        var checkup = CheckupFor(minimumVersion: "26.5");
        var result = checkup.MissingXcodeResult(new[] { Xcode("26.2", "17C52", "/Applications/Xcode.app") });

        var check = CheckCommand.BuildHealthCheck(checkup, result);

        Assert.Equal("Xcode 26.2 (17C52) is installed, but Xcode 26.5 is required.", check.Message);
    }

    [Fact]
    public void TheSelectedXcodeIsListedOnceEvenWhenAlsoFound()
    {
        var installs = new[]
        {
            Xcode("26.2", "17C52", "/Applications/Xcode.app"),
            Xcode("26.2", "17C52", "/Applications/Xcode.app", selected: true),
            Xcode("16.4", "16F6", "/Applications/Xcode-beta.app"),
        };

        var result = CheckupFor(minimumVersion: "26.5").MissingXcodeResult(installs);

        Assert.Equal("Xcode 26.2 (17C52), 16.4 (16F6) are installed, but Xcode 26.5 is required.", result.Message);
    }

    [Fact]
    public void NoXcodeIsReportedAsNotInstalled()
    {
        var result = CheckupFor(minimumVersion: "26.5").MissingXcodeResult(Array.Empty<XCodeInfo>());

        Assert.Equal("Xcode is not installed. Xcode 26.5 is required.", result.Message);
    }

    [Fact]
    public void AnInstallWithoutAShortVersionIsStillNamed()
    {
        // The Info.plist fallback leaves the short version empty when CFBundleShortVersionString is missing.
        var withoutShortVersion = new XCodeInfo(NuGetVersion.Parse("26.5"), null!, string.Empty, "/Applications/Xcode-26.5.0.app", false);
        var withoutAnyVersion = new XCodeInfo(null!, null!, string.Empty, "/Applications/Xcode-26.5.0.app", false);

        Assert.Equal("26.5", XCodeCheckup.FormatInstalledVersion(withoutShortVersion));
        Assert.Equal("/Applications/Xcode-26.5.0.app", XCodeCheckup.FormatInstalledVersion(withoutAnyVersion));
    }

    [Fact]
    public void RenamedXcodeInstallsAreLookedAtAfterTheUsualPaths()
    {
        var found = new[] { "/Applications/Xcode_27.0.app", "/Applications/Xcode.app", "/Applications/Xcode-26.5.0.app" };

        Assert.Equal(
            new[] { "/Applications/Xcode.app", "/Applications/Xcode-beta.app", "/Applications/Xcode-26.5.0.app", "/Applications/Xcode_27.0.app" },
            XCodeCheckup.OrderXcodePaths(found));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACutOffPlistIsSkippedRatherThanFailingTheCheck(bool binary)
    {
        var plist = new NSDictionary
        {
            { "CFBundleShortVersionString", "26.5" },
            { "CFBundleVersion", "24109" },
            { "ProductBuildVersion", "17F42" },
        };
        var bytes = binary
            ? BinaryPropertyListWriter.WriteToArray(plist)
            : System.Text.Encoding.UTF8.GetBytes(plist.ToXmlPropertyList());
        // Binary: the start (format detection) and the end (offset table and trailer, where plist-cil throws
        // IndexOutOfRangeException). The lengths in between all hit the same slow ArgumentOutOfRangeException path.
        var lengths = binary
            ? Enumerable.Range(0, 12).Concat(Enumerable.Range(bytes.Length - 12, 12))
            : Enumerable.Range(0, bytes.Length);
        var file = Path.GetTempFileName();

        try
        {
            // A truncated plist, like a half-extracted Xcode, must be skipped rather than throw out of the checkup.
            foreach (var length in lengths)
            {
                File.WriteAllBytes(file, bytes[..length]);

                var exception = Record.Exception(() => XCodeCheckup.TryReadPlist(file));

                Assert.True(exception is null, $"{length}/{bytes.Length} bytes threw {exception?.GetType().Name}: {exception?.Message}");
            }

            File.WriteAllBytes(file, bytes);
            Assert.Equal("26.5", XCodeCheckup.TryReadPlist(file)?.ObjectForKey("CFBundleShortVersionString")?.ToString());
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void ABuildNameWithMarkupCharactersDoesNotBreakTheRecommendation()
    {
        var suggestion = CheckupFor(minimumVersion: "26.5", minimumVersionName: "26.5 [beta]").DownloadSuggestion;

        // The recommendation line renders the suggestion as Spectre markup; unescaped brackets would throw here.
        var markup = new Spectre.Console.Markup($"[blue] {suggestion}[/]");

        Assert.NotNull(markup);
    }
}
