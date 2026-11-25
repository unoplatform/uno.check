using DotNetCheck.Cli;
using DotNetCheck.DotNet;
using DotNetCheck.Models;
using NuGet.Versioning;
using ManifestSdk = DotNetCheck.Manifest.DotNetSdk;

namespace UnoCheck.Tests;

/// <summary>
/// The SDK that checks and installs workloads has to be the one the project builds with. uno-check used
/// the newest installed SDK, so a machine with .NET 11 installed beside a project pinned to .NET 10 got
/// the workloads in the wrong feature band (issue #638).
/// </summary>
public class DotNetSdkSelectionTests
{
    static readonly DotNetSdkInfo[] RunnerSdks = Sdks("8.0.428", "9.0.313", "10.0.115", "10.0.404", "11.0.100");

    static DotNetSdkInfo[] Sdks(params string[] versions)
        => versions.Select(v => new DotNetSdkInfo(v, Path.Combine(Path.GetTempPath(), "dotnet", "sdk", v))).ToArray();

    static ManifestSdk[] Manifest(string version, bool requireExact = false)
        => [new ManifestSdk { Version = version, RequireExact = requireExact }];

    static GlobalJsonSdkResolution Resolved(string version)
        => new("global.json", NuGetVersion.Parse(version), null);

    static string Version(DotNetSdkSelection selection)
        => selection.Sdk!.Version.ToString();

    [Fact]
    public void Global_Json_Wins_Over_A_Newer_Installed_Sdk()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("10.0.101"), null, Resolved("10.0.404"));

        Assert.Equal("10.0.404", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.GlobalJson, selection.Source);
        Assert.Empty(selection.Warnings);
    }

    [Fact]
    public void Without_A_Global_Json_The_Newest_Sdk_Of_The_Manifests_Dotnet_Is_Used_Not_A_Later_Major()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("10.0.101"), null, null);

        Assert.Equal("10.0.404", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.Manifest, selection.Source);
    }

    [Fact]
    public void Without_A_Global_Json_A_Manifest_For_The_Newest_Dotnet_Uses_It()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("11.0.100"), null, null);

        Assert.Equal("11.0.100", Version(selection));
    }

    [Fact]
    public void Without_A_Compatible_Sdk_The_Newest_Installed_Is_Used_As_Before()
    {
        var selection = DotNetSdkSelector.Select(Sdks("8.0.428", "9.0.313"), Manifest("10.0.101"), null, null);

        Assert.Equal("9.0.313", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.Newest, selection.Source);
        Assert.Contains("10.0.101", Assert.Single(selection.Warnings));
    }

    [Fact]
    public void A_Global_Json_Sdk_Older_Than_The_Manifests_Is_Used_Because_Its_Band_Is_The_One_To_Install_Into()
    {
        var selection = DotNetSdkSelector.Select(Sdks("10.0.100", "10.0.404"), Manifest("10.0.103"), null, Resolved("10.0.100"));

        Assert.Equal("10.0.100", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.GlobalJson, selection.Source);
        Assert.Empty(selection.Warnings);
    }

    [Fact]
    public void An_Exact_Manifest_Sdk_Is_Not_Replaced_By_A_Newer_Patch()
    {
        var selection = DotNetSdkSelector.Select(Sdks("10.0.101", "10.0.404"), Manifest("10.0.101", requireExact: true), null, null);

        Assert.Equal("10.0.101", Version(selection));
    }

    [Fact]
    public void A_Global_Json_For_Another_Dotnet_Than_The_Manifests_Warns_And_Keeps_The_Manifests_Sdk()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("11.0.100"), null, Resolved("10.0.404"));

        Assert.Equal("11.0.100", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.Manifest, selection.Source);
        var warning = Assert.Single(selection.Warnings);
        Assert.Contains("10.0.404", warning);
        Assert.Contains("11.0.100", warning);
        Assert.Contains("NETSDK1147", warning);
    }

    [Fact]
    public void A_Global_Json_That_Did_Not_Resolve_Warns_And_Is_Ignored()
    {
        var selection = DotNetSdkSelector.Select(
            RunnerSdks,
            Manifest("10.0.101"),
            null,
            new GlobalJsonSdkResolution("global.json", null, "A compatible .NET SDK was not found."));

        Assert.Equal("10.0.404", Version(selection));
        Assert.Contains("A compatible .NET SDK was not found.", Assert.Single(selection.Warnings));
    }

    [Fact]
    public void A_Global_Json_Sdk_That_Is_Not_Listed_Warns_And_Is_Ignored()
    {
        var selection = DotNetSdkSelector.Select(Sdks("10.0.404"), Manifest("10.0.101"), null, Resolved("10.0.999"));

        Assert.Equal("10.0.404", Version(selection));
        Assert.Contains("10.0.999", Assert.Single(selection.Warnings));
    }

    [Fact]
    public void A_Requested_Sdk_Wins_Over_Global_Json()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("10.0.101"), "10.0.115", Resolved("10.0.404"));

        Assert.Equal("10.0.115", Version(selection));
        Assert.Equal(DotNetSdkSelectionSource.Requested, selection.Source);
        Assert.Null(selection.Error);
    }

    [Fact]
    public void A_Requested_Sdk_That_Is_Not_Installed_Fails_Naming_What_Is()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("10.0.101"), "10.0.999", null);

        Assert.Null(selection.Sdk);
        Assert.Contains("10.0.999", selection.Error);
        Assert.Contains("10.0.404", selection.Error);
    }

    [Fact]
    public void A_Requested_Sdk_The_Manifest_Cannot_Install_Workloads_For_Fails()
    {
        var selection = DotNetSdkSelector.Select(RunnerSdks, Manifest("11.0.100"), "10.0.404", null);

        Assert.Null(selection.Sdk);
        Assert.Contains("--manifest", selection.Error);
    }

    [Fact]
    public void No_Installed_Sdk_Selects_Nothing()
    {
        var selection = DotNetSdkSelector.Select([], Manifest("10.0.101"), null, Resolved("10.0.404"));

        Assert.Null(selection.Sdk);
        Assert.Null(selection.Error);
    }

    [Theory]
    [InlineData("10.0.404", "10.0.101", false, true)]
    [InlineData("10.0.100", "10.0.101", false, false)]
    [InlineData("11.0.100", "10.0.101", false, false)]
    [InlineData("10.0.404", "10.0.101", true, false)]
    [InlineData("10.0.101", "10.0.101", true, true)]
    public void Compatibility_Is_The_Same_Dotnet_And_Not_Older_Than_The_Manifest_Requires(string sdk, string required, bool requireExact, bool expected)
        => Assert.Equal(expected, DotNetSdkSelector.IsCompatible(NuGetVersion.Parse(sdk), Manifest(required, requireExact)));
}

public sealed class GlobalJsonDiscoveryTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "uno-check-globaljson-" + Guid.NewGuid().ToString("N"));

    public GlobalJsonDiscoveryTests() => Directory.CreateDirectory(Path.Combine(root, "src", "app"));

    public void Dispose() => Directory.Delete(root, recursive: true);

    string Write(string relativeDirectory, string content)
    {
        var path = Path.Combine(root, relativeDirectory, "global.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void A_Global_Json_In_A_Parent_Directory_Applies()
    {
        var path = Write("", """{ "sdk": { "version": "10.0.402", "rollForward": "latestFeature" } }""");

        Assert.Equal(path, DotNetSdkSelector.FindGlobalJsonPin(Path.Combine(root, "src", "app")));
    }

    [Fact]
    public void The_Nearest_Global_Json_Is_The_Only_One_That_Applies()
    {
        Write("", """{ "sdk": { "version": "10.0.402" } }""");
        Write("src", """{ "msbuild-sdks": { "Uno.Sdk": "6.0.0" } }""");

        Assert.Null(DotNetSdkSelector.FindGlobalJsonPin(Path.Combine(root, "src", "app")));
    }

    [Fact]
    public void Comments_And_Trailing_Commas_Are_Tolerated_As_The_Host_Does()
    {
        var path = Write("src", """
            {
              // pinned for CI
              "sdk": { "version": "10.0.402", },
            }
            """);

        Assert.Equal(path, DotNetSdkSelector.FindGlobalJsonPin(Path.Combine(root, "src", "app")));
    }

    [Fact]
    public void A_Global_Json_That_Is_Not_Json_Does_Not_Pin_An_Sdk()
    {
        Write("src", "not json");

        Assert.Null(DotNetSdkSelector.FindGlobalJsonPin(Path.Combine(root, "src", "app")));
    }

    [Fact]
    public void Dotnet_Version_Output_Is_Read_Past_Banners()
    {
        var version = DotNetSdkSelector.ParseDotNetVersionOutput(["Welcome to .NET 10.0!", "10.0.404"]);

        Assert.Equal(NuGetVersion.Parse("10.0.404"), version);
        Assert.Null(DotNetSdkSelector.ParseDotNetVersionOutput(["A compatible .NET SDK was not found."]));
    }
}

public class PinnedSdkVersionTests
{
    static SharedState StateWith(DotNetSdkSelectionSource source)
    {
        var state = new SharedState();
        var sdk = new DotNetSdkInfo("10.0.404", Path.Combine(Path.GetTempPath(), "dotnet", "sdk", "10.0.404"));
        state.ContributeState("dotnet", StateKey.DotNetSdkSelection, new DotNetSdkSelection(sdk, source, []));
        return state;
    }

    [Theory]
    [InlineData("Requested", "dotnet", "10.0.404")]
    [InlineData("GlobalJson", "dotnet", "10.0.404")]
    [InlineData("Manifest", "dotnet", null)]
    [InlineData("Manifest", "dotnetworkloads-10.0.101", "10.0.404")]
    [InlineData("Newest", "dotnetworkloads-10.0.101", null)]
    public void A_Fix_Is_Given_The_Selected_Sdk_Unless_It_Could_Change_Or_Not_Be_Requested(string source, string checkupId, string? expected)
        => Assert.Equal(expected, CheckCommand.GetPinnedSdkVersion(StateWith(Enum.Parse<DotNetSdkSelectionSource>(source)), checkupId));

    [Fact]
    public void Nothing_Is_Pinned_Before_The_Dotnet_Checkup_Ran()
        => Assert.Null(CheckCommand.GetPinnedSdkVersion(new SharedState(), "dotnetworkloads-10.0.101"));
}

public class WorkloadManagerSdkPinTests
{
    [Fact]
    public void An_Exact_Sdk_Is_Pinned_So_The_Cli_Cannot_Roll_Forward_To_Another_Band()
    {
        var globalJson = DotNetWorkloadManager.BuildGlobalJson("10.0.404", exactSdkVersion: true);

        Assert.Equal("10.0.404", globalJson.Sdk.Version);
        Assert.Equal("disable", globalJson.Sdk.RollForward);
        Assert.True(globalJson.Sdk.AllowPrerelease);
    }

    [Fact]
    public void A_Minimum_Sdk_Rolls_Forward_As_Before()
    {
        var globalJson = DotNetWorkloadManager.BuildGlobalJson("10.0.101", exactSdkVersion: false);

        Assert.Equal("latestFeature", globalJson.Sdk.RollForward);
    }
}
