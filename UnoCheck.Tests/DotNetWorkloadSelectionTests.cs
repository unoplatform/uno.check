using System.Text.Json;
using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.DotNet;
using DotNetCheck.Manifest;

namespace UnoCheck.Tests;

public class DotNetWorkloadSelectionTests
{
    [Theory]
    [InlineData("maui-ios", TargetPlatform.iOS, true)]
    [InlineData("maui-ios", TargetPlatform.Android, false)]
    [InlineData("maui-ios", TargetPlatform.macOS, false)]
    [InlineData("maui-android", TargetPlatform.Android, true)]
    [InlineData("maui-android", TargetPlatform.iOS, false)]
    [InlineData("maui", TargetPlatform.macOS, true)]
    [InlineData("maccatalyst", TargetPlatform.macOS, true)]
    [InlineData("maccatalyst", TargetPlatform.iOS, false)]
    [InlineData("tvos", TargetPlatform.tvOS, true)]
    [InlineData("tvos", TargetPlatform.iOS, false)]
    [InlineData("wasm-tools", TargetPlatform.WebAssembly, true)]
    [InlineData("wasm-tools", TargetPlatform.SkiaDesktop, false)]
    [InlineData("wasm-tools-net10", TargetPlatform.WebAssembly, true)]
    [InlineData("wasm-tools-net9", TargetPlatform.WebAssembly, true)]
    [InlineData("wasm-tools-net10", TargetPlatform.SkiaDesktop, false)]
    [InlineData("wasm-tools-net10", TargetPlatform.Android, false)]
    [InlineData("wasm-tools-net", TargetPlatform.WebAssembly, false)]
    [InlineData("wasm-tools-net+10", TargetPlatform.WebAssembly, false)]
    [InlineData("unknown-workload", TargetPlatform.All, false)]
    public void IsRequiredFor_SelectsWorkloadsByTarget(string workloadId, TargetPlatform targets, bool expected)
    {
        var actual = DotNetWorkloadsCheckup.IsRequiredFor(workloadId, targets, skipMaui: false);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("maui")]
    [InlineData("maui-android")]
    [InlineData("maui-ios")]
    public void IsRequiredFor_SkipMaui_ExcludesEveryMauiWorkload(string workloadId)
    {
        var actual = DotNetWorkloadsCheckup.IsRequiredFor(workloadId, TargetPlatform.All, skipMaui: true);

        Assert.False(actual);
    }

    [Theory]
    [MemberData(nameof(ManifestVersionGuardTests.EmbeddedManifestResourceNames), MemberType = typeof(ManifestVersionGuardTests))]
    public async Task EmbeddedManifest_EveryWorkloadIsSelectableForSomeTarget(string manifestResourceName)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);
        var workloadIds = manifest.Check.DotNet.Sdks.SelectMany(sdk => sdk.Workloads ?? []).Select(w => w.Id).ToArray();

        var unselectable = workloadIds
            .Where(id => !DotNetWorkloadsCheckup.IsRequiredFor(id, TargetPlatform.All, skipMaui: false))
            .ToArray();

        Assert.True(
            unselectable.Length == 0,
            $"{manifestResourceName} declares workloads that no target selects, so they are never installed: "
            + string.Join(", ", unselectable));
    }

    [Fact]
    public void BuildRollbackJson_WorkloadsSharingAManifest_WriteItOnce()
    {
        var workloads = new[]
        {
            new DotNetWorkload { Id = "android", WorkloadManifestId = "microsoft.net.sdk.android", Version = "37.2.0-rc.2.84/11.0.100-rc.2" },
            new DotNetWorkload { Id = "maui-android", WorkloadManifestId = "microsoft.net.sdk.maui", Version = "11.0.0-rc.2.26505.5/11.0.100-rc.2" },
            new DotNetWorkload { Id = "maui-ios", WorkloadManifestId = "microsoft.net.sdk.maui", Version = "11.0.0-rc.2.26505.5/11.0.100-rc.2" },
        };

        var json = DotNetWorkloadManager.BuildRollbackJson(workloads);

        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString())).ToArray();
        Assert.Equal(
            [
                ("microsoft.net.sdk.android", "37.2.0-rc.2.84/11.0.100-rc.2"),
                ("microsoft.net.sdk.maui", "11.0.0-rc.2.26505.5/11.0.100-rc.2"),
            ],
            entries);
    }

    [Fact]
    public void BuildRollbackJson_WorkloadsSharingAManifestWithDifferentVersions_Throws()
    {
        var workloads = new[]
        {
            new DotNetWorkload { Id = "maui-android", WorkloadManifestId = "microsoft.net.sdk.maui", Version = "11.0.0/11.0.100" },
            new DotNetWorkload { Id = "maui-ios", WorkloadManifestId = "microsoft.net.sdk.maui", Version = "11.0.1/11.0.100" },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => DotNetWorkloadManager.BuildRollbackJson(workloads));

        Assert.Contains("microsoft.net.sdk.maui", exception.Message);
        Assert.Contains("11.0.0/11.0.100", exception.Message);
        Assert.Contains("11.0.1/11.0.100", exception.Message);
    }

    [Theory]
    [MemberData(nameof(ManifestVersionGuardTests.EmbeddedManifestResourceNames), MemberType = typeof(ManifestVersionGuardTests))]
    public async Task EmbeddedManifest_WorkloadsSharingAManifestUseOneVersion(string manifestResourceName)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);

        foreach (var sdk in manifest.Check.DotNet.Sdks)
        {
            var exception = Record.Exception(() => DotNetWorkloadManager.BuildRollbackJson(sdk.Workloads ?? []));

            Assert.Null(exception);
        }
    }

    [Theory]
    [InlineData(TargetPlatform.macOS)]
    [InlineData(TargetPlatform.macOS | TargetPlatform.Android)]
    public async Task GetMacCatalystTargetWarning_PreMajorWithMacOSTarget_Warns(TargetPlatform targets)
    {
        var manifest = await Manifest.FromEmbeddedResource(Manifest.PreviewMajorManifestResourceName);
        var workloads = manifest.Check.DotNet.Sdks.SelectMany(sdk => sdk.Workloads ?? []);

        var warning = DotNetWorkloadsCheckup.GetMacCatalystTargetWarning(workloads, targets);

        Assert.NotNull(warning);
        Assert.Contains("--target desktop", warning);
    }

    [Theory]
    [InlineData(Manifest.PreviewMajorManifestResourceName, TargetPlatform.All)]
    [InlineData(Manifest.PreviewMajorManifestResourceName, TargetPlatform.Android | TargetPlatform.iOS)]
    [InlineData(Manifest.PreviewMajorManifestResourceName, TargetPlatform.None)]
    [InlineData(Manifest.DefaultManifestResourceName, TargetPlatform.macOS)]
    [InlineData(Manifest.PreviewManifestResourceName, TargetPlatform.macOS)]
    public async Task GetMacCatalystTargetWarning_NoExplicitMacOSTargetOrManifestHasMacCatalyst_DoesNotWarn(string manifestResourceName, TargetPlatform targets)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);
        var workloads = manifest.Check.DotNet.Sdks.SelectMany(sdk => sdk.Workloads ?? []);

        var warning = DotNetWorkloadsCheckup.GetMacCatalystTargetWarning(workloads, targets);

        Assert.Null(warning);
    }
}
