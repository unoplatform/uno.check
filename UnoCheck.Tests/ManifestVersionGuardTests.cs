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
