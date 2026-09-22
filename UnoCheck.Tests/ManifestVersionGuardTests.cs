using DotNetCheck.Manifest;
using NuGet.Versioning;

namespace UnoCheck.Tests;

public class ManifestVersionGuardTests
{
    public static IEnumerable<object[]> EmbeddedManifestResourceNames =>
    [
        [Manifest.DefaultManifestResourceName],
        [Manifest.PreviewManifestResourceName],
        [Manifest.PreviewMajorManifestResourceName]
    ];

    [Theory]
    [MemberData(nameof(EmbeddedManifestResourceNames))]
    public async Task EmbeddedManifest_HasValidDotNetAndWasmToolsVersionFormats(string manifestResourceName)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);

        Assert.NotNull(manifest?.Check?.Variables);
        Assert.True(manifest.Check.Variables.TryGetValue("DOTNET_SDK_VERSION", out var dotnetSdkVersion));
        var sdkVersion = dotnetSdkVersion?.ToString();
        Assert.NotNull(sdkVersion);
        Assert.True(NuGetVersion.TryParse(sdkVersion, out _));

        Assert.True(manifest.Check.Variables.TryGetValue("WASMTOOLS_VERSION", out var wasmToolsVersion));
        var wasmVersion = wasmToolsVersion?.ToString();
        Assert.NotNull(wasmVersion);

        var wasmVersionParts = wasmVersion!.Split('/');
        Assert.Equal(2, wasmVersionParts.Length);
        Assert.True(NuGetVersion.TryParse(wasmVersionParts[0], out _));
        Assert.True(NuGetVersion.TryParse(wasmVersionParts[1], out _));
    }

    [Theory]
    [MemberData(nameof(EmbeddedManifestResourceNames))]
    public async Task EmbeddedManifest_RequiredSdkSharesTheFeatureBandOfItsWorkloads(string manifestResourceName)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);
        var sdks = manifest?.Check?.DotNet?.Sdks;

        Assert.NotNull(sdks);

        foreach (var sdk in sdks!)
        {
            Assert.True(NuGetVersion.TryParse(sdk.Version, out var sdkVersion), $"Unparsable SDK version '{sdk.Version}'.");

            var sdkBand = FeatureBandOf(sdkVersion!);
            var mismatched = (sdk.Workloads ?? [])
                .Where(workload => workload.Version?.Contains('/') == true)
                .Select(workload => (workload.Id, Band: workload.Version!.Split('/')[1]))
                .Where(workload => workload.Band != sdkBand)
                .ToArray();

            Assert.True(
                mismatched.Length == 0,
                $"SDK {sdk.Version} is in feature band {sdkBand}, but these workload manifests are pinned to "
                + $"another band: {string.Join(", ", mismatched.Select(w => $"{w.Id} -> {w.Band}"))}. "
                + "A workload manifest from a different band is invisible to the SDK, so consumers fail with "
                + "NETSDK1147, and --fix installs the out-of-band SDK over the one they provisioned.");
        }
    }

    /// <summary>
    /// The SDK feature band, as it appears after the slash in a workload manifest version:
    /// 10.0.108 -> 10.0.100, 10.0.302 -> 10.0.300, 11.0.100-rc.1.26425.128 -> 11.0.100-rc.1.
    /// </summary>
    private static string FeatureBandOf(NuGetVersion version)
    {
        var band = $"{version.Major}.{version.Minor}.{version.Patch / 100 * 100}";

        if (!version.IsPrerelease)
        {
            return band;
        }

        // A band keeps only the channel and its number, dropping the build labels.
        var labels = version.ReleaseLabels.Take(2).ToArray();

        return labels.Length == 0 ? band : $"{band}-{string.Join('.', labels)}";
    }

    [Theory]
    [MemberData(nameof(EmbeddedManifestResourceNames))]
    public async Task EmbeddedManifest_DefinesWasmToolsWorkloadForWebAssembly(string manifestResourceName)
    {
        var manifest = await Manifest.FromEmbeddedResource(manifestResourceName);
        var workloads = manifest?.Check?.DotNet?.Sdks?.SelectMany(sdk => sdk.Workloads ?? [])?.ToArray();

        Assert.NotNull(workloads);
        Assert.True(manifest!.Check.Variables.TryGetValue("WASMTOOLS_VERSION", out var expectedWasmToolsVersion));

        Assert.Contains(workloads!, workload =>
            workload.Id == "wasm-tools"
            && workload.WorkloadManifestId == "microsoft.net.workload.mono.toolchain.current"
            && workload.Version == expectedWasmToolsVersion?.ToString());
    }
}
