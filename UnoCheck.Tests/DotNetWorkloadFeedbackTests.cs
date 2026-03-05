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

    [Fact]
    public void BuildCliFailureMessage_WhenDiskIsFull_IncludesActionableGuidance()
    {
        var message = DotNetWorkloadManager.BuildCliFailureMessage(
            operationName: "Workload Install",
            command: "dotnet workload install ...",
            output: "Workload installation failed: One or more errors occurred. (No space left on device : '/tmp/foo')");

        Assert.Contains("Insufficient disk space", message);
        Assert.Contains("temporary folders", message);
        Assert.Contains("uno-check --fix", message);
    }

    [Fact]
    public void BuildCliFailureMessage_WhenDiskIsFullOnWindowsStyleError_IncludesActionableGuidance()
    {
        var message = DotNetWorkloadManager.BuildCliFailureMessage(
            operationName: "Workload Install",
            command: "dotnet workload install ...",
            output: "ERROR: There is not enough space on the disk.");

        Assert.Contains("Insufficient disk space", message);
        Assert.Contains("uno-check --fix", message);
    }

    [Fact]
    public void BuildCliFailureMessage_WhenGenericFailure_UsesRelevantFailureLine()
    {
        var message = DotNetWorkloadManager.BuildCliFailureMessage(
            operationName: "Workload Install",
            command: "dotnet workload install ...",
            output: "line one\nline two\nWorkload installation failed with exit code 1");

        Assert.Contains("Workload Install failed", message);
        Assert.Contains("Workload installation failed with exit code 1", message);
        Assert.Contains("dotnet workload install", message);
    }
}
