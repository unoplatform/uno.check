using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.Cli;
using DotNetCheck.Manifest;
using NuGet.Versioning;

namespace UnoCheck.Tests;

/// <summary>
/// Microsoft OpenJDK 17.0.21.0.2 reports a five-part version, which NuGetVersion can't parse. The checkup dropped
/// that JDK without a word, then said OpenJDK was missing although a newer javac than the required one was installed.
/// </summary>
public class OpenJdkCheckupTests
{
    static OpenJdkCheckup CheckupFor(string version, bool requireExact = false)
        => new()
        {
            Manifest = new Manifest
            {
                Check = new Check
                {
                    OpenJdk = new OpenJdk { Version = version, RequireExact = requireExact },
                },
            },
        };

    static string Home(string path)
        => Path.GetFullPath(path);

    static OpenJdkInfo Jdk(string home, string? version)
        => new(Path.Join(Home(home), "bin", "javac"), version is null ? null! : NuGetVersion.Parse(version));

    [Theory]
    [InlineData("javac 17.0.16", "17.0.16")]
    [InlineData("javac 17.0.20.1", "17.0.20.1")]
    [InlineData("javac 17.0.21.0.2", "17.0.21.0.2")]
    public void TheVersionIsReadAsJavacReportsIt(string output, string expected)
    {
        Assert.True(OpenJdkCheckup.TryParseJavaCVersion(output, out var version));
        Assert.Equal(expected, version.ToString());
    }

    [Fact]
    public void AFivePartVersionIsComparedOnItsFirstFourParts()
    {
        OpenJdkCheckup.TryParseJavaCVersion("javac 17.0.21.0.2", out var version);

        Assert.True(version.IsCompatible(NuGetVersion.Parse("17.0.19")));
        Assert.True(version > NuGetVersion.Parse("17.0.20.1"));
        Assert.False(version > NuGetVersion.Parse("17.0.22"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("javac: error")]
    [InlineData("The operation couldn't be completed. Unable to locate a Java Runtime.")]
    public void OutputWithoutAVersionIsNotRead(string? output)
    {
        Assert.False(OpenJdkCheckup.TryParseJavaCVersion(output!, out var version));
        Assert.Null(version);
    }

    [Fact]
    public void NoJavacIsReportedMissing()
    {
        var result = CheckupFor("17.0.19").NoCompatibleJdkResult(Array.Empty<OpenJdkInfo>());

        Assert.Equal(DotNetCheck.Models.Status.Error, result.Status);
        Assert.Null(result.Message);
        Assert.Equal("Install OpenJDK17", result.Suggestion.Name);
    }

    [Fact]
    public void AnOlderJdkIsNamedRatherThanReportedMissing()
    {
        var result = CheckupFor("17.0.19").NoCompatibleJdkResult(new[] { Jdk("/usr/lib/jvm/temurin-17-jdk-amd64", "17.0.16") });

        Assert.Equal(
            $"OpenJDK 17.0.19 or later is required, but none of the JDKs found is compatible: 17.0.16 ({Home("/usr/lib/jvm/temurin-17-jdk-amd64")}).",
            result.Message);
        Assert.DoesNotContain("missing", result.Suggestion.Description ?? string.Empty);
    }

    [Fact]
    public void AJavacWhoseVersionIsNotRecognizedIsNamed()
    {
        var unreadable = Jdk("/usr/lib/jvm/msopenjdk-17-amd64", null);
        var found = new[] { Jdk("/usr/lib/jvm/temurin-17-jdk-amd64", "17.0.16"), unreadable };

        var result = CheckupFor("17.0.19").NoCompatibleJdkResult(found);

        Assert.Equal(
            "OpenJDK 17.0.19 or later is required, but none of the JDKs found is compatible: "
            + $"17.0.16 ({Home("/usr/lib/jvm/temurin-17-jdk-amd64")}), {unreadable.JavaC.FullName} (version not recognized).",
            result.Message);
    }

    [Fact]
    public void AnExactRequirementIsNotPhrasedAsAMinimum()
    {
        var message = OpenJdkCheckup.FormatIncompatibleMessage(
            CheckupFor("17.0.19", requireExact: true).Requirement, new[] { Jdk("/opt/jdk", "17.0.20") });

        Assert.Equal($"OpenJDK 17.0.19 is required, but none of the JDKs found is compatible: 17.0.20 ({Home("/opt/jdk")}).", message);
    }

    [Fact]
    public void TheJsonReportCarriesTheReasonNotOnlyTheSuggestion()
    {
        var checkup = CheckupFor("17.0.19");
        var result = checkup.NoCompatibleJdkResult(new[] { Jdk("/usr/lib/jvm/temurin-17-jdk-amd64", "17.0.16") });

        var check = CheckCommand.BuildHealthCheck(checkup, result);

        Assert.Equal(result.Message, check.Message);
    }
}
