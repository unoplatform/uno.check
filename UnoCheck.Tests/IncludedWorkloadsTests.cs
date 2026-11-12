using DotNetCheck.Checkups;
using DotNetCheck.DotNet;

namespace UnoCheck.Tests;

public class IncludedWorkloadsTests
{
    // What `dotnet workload list` reports on a machine set up by the default channel (issue #614).
    static readonly string[] DefaultChannelInstall = ["android", "ios", "maccatalyst", "tvos", "maui", "wasm-tools"];

    [Theory]
    [InlineData("maui-android")]
    [InlineData("maui-ios")]
    public void FindInstalledProvider_DefaultChannelInstall_ProvidesPreMajorMauiWorkloads(string workloadId)
    {
        var provider = IncludedWorkloads.FindInstalledProvider(workloadId, DefaultChannelInstall);

        Assert.Equal("maui", provider);
    }

    [Theory]
    [InlineData("android")]
    [InlineData("maui")]
    [InlineData("wasm-tools")]
    public void FindInstalledProvider_InstalledByName_ReturnsTheWorkload(string workloadId)
    {
        var provider = IncludedWorkloads.FindInstalledProvider(workloadId, DefaultChannelInstall);

        Assert.Equal(workloadId, provider);
    }

    [Theory]
    [InlineData("android", "maui-android")]
    [InlineData("ios", "maui-ios")]
    [InlineData("maccatalyst", "maui-maccatalyst")]
    [InlineData("maui-android", "maui-mobile")]
    public void FindInstalledProvider_ExtendingWorkloadInstalled_ReturnsIt(string workloadId, string installedWorkloadId)
    {
        var provider = IncludedWorkloads.FindInstalledProvider(workloadId, [installedWorkloadId]);

        Assert.Equal(installedWorkloadId, provider);
    }

    [Theory]
    // maui also needs maui-maccatalyst and maui-windows, which a pre-major install lacks.
    [InlineData("maui", new[] { "android", "ios", "tvos", "maui-android", "maui-ios", "wasm-tools" })]
    [InlineData("maui-ios", new[] { "maui-android" })]
    [InlineData("tvos", new[] { "maui" })]
    [InlineData("wasm-tools", new[] { "maui" })]
    [InlineData("maui-android", new string[0])]
    public void FindInstalledProvider_NothingInstalledProvidesIt_ReturnsNull(string workloadId, string[] installedWorkloadIds)
    {
        var provider = IncludedWorkloads.FindInstalledProvider(workloadId, installedWorkloadIds);

        Assert.Null(provider);
    }

    [Fact]
    public void FindInstalledProvider_IgnoresCase()
    {
        var provider = IncludedWorkloads.FindInstalledProvider("Maui-Android", ["MAUI"]);

        Assert.Equal("MAUI", provider);
    }

    [Fact]
    public void FormatInstalledStatus_InstalledByName_NamesTheWorkload()
    {
        var status = DotNetWorkloadsCheckup.FormatInstalledStatus("maui", "maui", "11.0.0", "11.0.100");

        Assert.Equal("maui (11.0.0/11.0.100) is installed.", status);
    }

    [Fact]
    public void FormatInstalledStatus_InstalledThroughAnotherWorkload_NamesTheProvider()
    {
        var status = DotNetWorkloadsCheckup.FormatInstalledStatus("maui-android", "maui", "11.0.0", "11.0.100");

        Assert.Equal("maui-android (11.0.0/11.0.100) is installed as part of the maui workload.", status);
    }
}
