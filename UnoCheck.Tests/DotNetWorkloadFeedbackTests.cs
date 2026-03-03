using DotNetCheck.Checkups;
using DotNetCheck.DotNet;

namespace UnoCheck.Tests;

public class DotNetWorkloadFeedbackTests
{
    [Theory]
    [InlineData(false, false, false, false, false, true)]
    [InlineData(true, false, false, false, false, false)]
    [InlineData(false, true, false, false, false, false)]
    [InlineData(false, false, true, false, false, false)]
    [InlineData(false, false, false, true, false, false)]
    [InlineData(false, false, false, false, true, false)]
    public void ShouldUseLiveSpinnerFor_ReturnsExpectedValue(
        bool verbose,
        bool ci,
        bool nonInteractive,
        bool outputRedirected,
        bool errorRedirected,
        bool expected)
    {
        var actual = DotNetWorkloadsCheckup.ShouldUseLiveSpinnerFor(verbose, ci, nonInteractive, outputRedirected, errorRedirected);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void BuildInstallArgs_AddsDetailedVerbosityWhenVerboseEnabled()
    {
        var args = DotNetWorkloadManager.BuildInstallArgs(
            sdkVersion: "10.0.103",
            rollbackFile: "c:\\temp\\workload.json",
            workloadIds: ["wasm-tools"],
            packageSources: ["https://api.nuget.org/v3/index.json"],
            verbose: true);

        Assert.Contains("workload", args);
        Assert.Contains("install", args);
        Assert.Contains("--verbosity", args);
        Assert.Contains("detailed", args);
    }

    [Fact]
    public void BuildInstallArgs_DoesNotAddVerbosityWhenVerboseDisabled()
    {
        var args = DotNetWorkloadManager.BuildInstallArgs(
            sdkVersion: "10.0.103",
            rollbackFile: "c:\\temp\\workload.json",
            workloadIds: ["wasm-tools"],
            packageSources: ["https://api.nuget.org/v3/index.json"],
            verbose: false);

        Assert.DoesNotContain("--verbosity", args);
        Assert.DoesNotContain("detailed", args);
    }

    [Fact]
    public void BuildRepairArgs_AddsDetailedVerbosityWhenVerboseEnabled()
    {
        var args = DotNetWorkloadManager.BuildRepairArgs(
            sdkVersion: "10.0.103",
            packageSources: ["https://api.nuget.org/v3/index.json"],
            verbose: true);

        Assert.Contains("workload", args);
        Assert.Contains("repair", args);
        Assert.Contains("--verbosity", args);
        Assert.Contains("detailed", args);
    }
}
