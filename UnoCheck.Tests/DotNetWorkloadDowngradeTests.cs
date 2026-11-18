using DotNetCheck.Checkups;
using DotNetCheck.Manifest;

namespace UnoCheck.Tests;

/// <summary>
/// <c>--fix</c> writes every required workload to the rollback file, and the SDK pins each manifest
/// in that file to the version given. A manifest already installed at a newer version must keep it,
/// so fixing one missing workload doesn't roll back another that works (#601).
/// </summary>
public class DotNetWorkloadDowngradeTests
{
    private static DotNetWorkload Workload(string id, string manifestId, string version) => new()
    {
        Id = id,
        WorkloadManifestId = manifestId,
        PackageId = $"{manifestId}.Manifest-11.0.100",
        Version = version,
        WorkloadVersion = "11.100.1",
        Abstract = false,
        IgnoredPackIds = ["Some.Pack"],
        SupportedPlatforms = ["Windows", "OSX"],
    };

    [Fact]
    public void WithoutDowngrades_InstalledManifestIsNewer_KeepsTheInstalledVersion()
    {
        var android = Workload("android", "microsoft.net.sdk.android", "37.2.31/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", "37.3.6", "11.0.100")]);

        Assert.Equal("37.3.6/11.0.100", Assert.Single(result).Version);
    }

    [Theory]
    [InlineData("37.2.31")]
    [InlineData("37.1.0")]
    [InlineData("37.2.31-rc.1")]
    [InlineData("not-a-version")]
    public void WithoutDowngrades_InstalledManifestIsNotNewer_KeepsThePin(string installedVersion)
    {
        var android = Workload("android", "microsoft.net.sdk.android", "37.2.31/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", installedVersion, "11.0.100")]);

        Assert.Equal("37.2.31/11.0.100", Assert.Single(result).Version);
    }

    [Fact]
    public void WithoutDowngrades_ManifestNotInstalled_KeepsThePin()
    {
        var wasm = Workload("wasm-tools", "microsoft.net.workload.mono.toolchain.current", "11.0.100/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades([wasm], []);

        Assert.Equal("11.0.100/11.0.100", Assert.Single(result).Version);
    }

    [Fact]
    public void WithoutDowngrades_PinIsNotAVersion_KeepsThePin()
    {
        var android = Workload("android", "microsoft.net.sdk.android", "$(ANDROID_SDK_VERSION)");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", "37.3.6", "11.0.100")]);

        Assert.Equal("$(ANDROID_SDK_VERSION)", Assert.Single(result).Version);
    }

    /// <summary>
    /// The #601 case: one workload is missing (its manifest is behind the pin), while another works on
    /// a newer manifest. Only the missing one moves to the pin.
    /// </summary>
    [Fact]
    public void WithoutDowngrades_OneMissingOneNewer_OnlyTheMissingOneUsesThePin()
    {
        var android = Workload("android", "microsoft.net.sdk.android", "37.2.31/11.0.100");
        var wasm = Workload("wasm-tools", "microsoft.net.workload.mono.toolchain.current", "11.0.100/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android, wasm],
            [
                ("microsoft.net.sdk.android", "37.3.6", "11.0.100"),
                ("microsoft.net.workload.mono.toolchain.current", "11.0.0-rc.3.26522.148", "11.0.100-rc.3"),
            ]);

        Assert.Equal(["37.3.6/11.0.100", "11.0.100/11.0.100"], result.Select(w => w.Version));
    }

    [Fact]
    public void WithoutDowngrades_WorkloadsSharingANewerManifest_AllKeepTheInstalledVersion()
    {
        var mauiAndroid = Workload("maui-android", "microsoft.net.sdk.maui", "11.0.0/11.0.100");
        var mauiIos = Workload("maui-ios", "microsoft.net.sdk.maui", "11.0.0/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [mauiAndroid, mauiIos],
            [("microsoft.net.sdk.maui", "11.0.10", "11.0.100")]);

        Assert.All(result, w => Assert.Equal("11.0.10/11.0.100", w.Version));
    }

    [Fact]
    public void WithoutDowngrades_InstalledManifestHasNoBand_WritesTheVersionAlone()
    {
        var android = Workload("android", "microsoft.net.sdk.android", "37.2.31/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", "37.3.6", "")]);

        Assert.Equal("37.3.6", Assert.Single(result).Version);
    }

    [Fact]
    public void WithoutDowngrades_MatchesManifestIdsIgnoringCase()
    {
        var android = Workload("android", "Microsoft.NET.Sdk.Android", "37.2.31/11.0.100");

        var result = DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", "37.3.6", "11.0.100")]);

        Assert.Equal("37.3.6/11.0.100", Assert.Single(result).Version);
    }

    [Fact]
    public void WithoutDowngrades_KeepsEveryOtherPropertyAndLeavesTheManifestUnchanged()
    {
        var android = Workload("android", "microsoft.net.sdk.android", "37.2.31/11.0.100");

        var kept = Assert.Single(DotNetWorkloadsCheckup.WithoutDowngrades(
            [android],
            [("microsoft.net.sdk.android", "37.3.6", "11.0.100")]));

        Assert.NotSame(android, kept);
        Assert.Equal("37.2.31/11.0.100", android.Version);
        Assert.Equal(android.Id, kept.Id);
        Assert.Equal(android.WorkloadManifestId, kept.WorkloadManifestId);
        Assert.Equal(android.PackageId, kept.PackageId);
        Assert.Equal(android.WorkloadVersion, kept.WorkloadVersion);
        Assert.Equal(android.Abstract, kept.Abstract);
        Assert.Equal(android.IgnoredPackIds, kept.IgnoredPackIds);
        Assert.Equal(android.SupportedPlatforms, kept.SupportedPlatforms);
    }
}
