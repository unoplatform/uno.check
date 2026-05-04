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

    [Theory]
    [InlineData("error: Permission denied", true)]
    [InlineData("Access to the path '/usr/share/dotnet' is denied.", true)]
    [InlineData("EACCES: permission denied, mkdir '/usr/share/dotnet'", true)]
    [InlineData("Administrator privileges are required to perform this operation.", true)]
    [InlineData("Inadequate permissions. Run the command with elevated privileges.", true)]
    [InlineData("Run the command with elevated privileges.", true)]
    [InlineData("No space left on device", false)]
    [InlineData("", false)]
    public void ShouldRetryWithSudo_ReturnsExpectedValue(string output, bool expected)
    {
        var actual = DotNetWorkloadManager.ShouldRetryWithSudo(output);

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("Restoring NuGet packages...\nDetermining projects to restore...\nerror: Permission denied\nRestore failed.", true)]
    [InlineData("Installing workload manifest microsoft.net.sdk.android...\nAccess to the path '/usr/local/share/dotnet/sdk-manifests' is denied.\nInstallation failed.", true)]
    [InlineData("Downloading microsoft.android.sdk.linux version 35.0.105...\nEACCES: permission denied, open '/usr/share/dotnet/packs/foo'\nWorkload installation failed.", true)]
    [InlineData("Downloading microsoft.android.sdk.linux version 35.0.105 failed\nThe feed 'https://api.nuget.org/v3/index.json' lists package", false)]
    public void ShouldRetryWithSudo_WithVerboseOutput_MatchesPermissionPatterns(string output, bool expected)
    {
        var actual = DotNetWorkloadManager.ShouldRetryWithSudo(output);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void IsSdkPathWritable_WritableDirectory_ReturnsTrue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "uno-check-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            Assert.True(DotNetWorkloadManager.IsSdkPathWritable(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsSdkPathWritable_NonExistentDirectory_ReturnsFalse()
    {
        var nonExistentDir = Path.Combine(Path.GetTempPath(), "uno-check-nonexistent-" + Guid.NewGuid().ToString("N"));

        Assert.False(DotNetWorkloadManager.IsSdkPathWritable(nonExistentDir));
    }

    [Fact]
    public async Task PrepareForInstallAsync_WhenSdkPathWritable_CompletesWithoutPrompt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "uno-check-prep-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var manager = new DotNetWorkloadManager(tempDir, "10.0.103");

            // The SDK path is writable, so PrepareForInstallAsync must early-return
            // and never invoke sudo.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await manager.PrepareForInstallAsync(cts.Token);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PrepareForInstallAsync_WhenSdkPathNotWritableAndNonInteractive_DoesNotPrompt()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "uno-check-noexist-" + Guid.NewGuid().ToString("N"));
        var previous = DotNetCheck.Util.NonInteractive;
        try
        {
            DotNetCheck.Util.NonInteractive = true;
            var manager = new DotNetWorkloadManager(nonExistentPath, "10.0.103");

            // Non-existent path → not writable. With NonInteractive=true, the helper
            // must early-return rather than invoke `sudo -v`, which would block
            // waiting for input on /dev/tty.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await manager.PrepareForInstallAsync(cts.Token);
        }
        finally
        {
            DotNetCheck.Util.NonInteractive = previous;
        }
    }

    [Fact]
    public void ParseInstalledWorkloadIds_ParsesMachineReadableJson()
    {
        var ids = DotNetWorkloadManager.ParseInstalledWorkloadIds(
            "{\"installed\":[\"wasm-tools\",\"android\"],\"updateAvailable\":[]}");

        Assert.Equal(["wasm-tools", "android"], ids);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json at all")]
    [InlineData("{\"installed\":null}")]
    public void ParseInstalledWorkloadIds_ReturnsEmptyOnUnparseableInput(string json)
    {
        var ids = DotNetWorkloadManager.ParseInstalledWorkloadIds(json);

        Assert.Empty(ids);
    }

    [Fact]
    public void CombineInstalledWorkloads_UserOnlySucceeds_ReturnsUserList()
    {
        var ids = DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: true,
            userInstalled: ["wasm-tools"],
            sudoContextAttempted: true,
            sudoContextSucceeded: false,
            sudoInstalled: null,
            failingCommand: "dotnet workload list --machine-readable");

        Assert.Equal(["wasm-tools"], ids);
    }

    [Fact]
    public void CombineInstalledWorkloads_SudoOnlySucceeds_ReturnsSudoList()
    {
        // Repro for the "Inadequate permissions" case: user-context call returns
        // non-zero before producing parseable JSON, but the sudo-context probe
        // discovers the root-owned workloads.
        var ids = DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: false,
            userInstalled: null,
            sudoContextAttempted: true,
            sudoContextSucceeded: true,
            sudoInstalled: ["android", "ios"],
            failingCommand: "dotnet workload list --machine-readable");

        Assert.Equal(["android", "ios"], ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CombineInstalledWorkloads_MixedUserAndSudoInstalls_UnionsBoth()
    {
        // Repro for mixed installs: one workload installed for the current user,
        // another previously installed via sudo. The combined result must include
        // both, otherwise the checkup flags the root-owned workload as missing.
        var ids = DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: true,
            userInstalled: ["wasm-tools", "maui"],
            sudoContextAttempted: true,
            sudoContextSucceeded: true,
            sudoInstalled: ["maui", "android"],
            failingCommand: "dotnet workload list --machine-readable");

        Assert.Equal(["android", "maui", "wasm-tools"], ids.OrderBy(x => x).ToArray());
    }

    [Fact]
    public void CombineInstalledWorkloads_DeduplicatesCaseInsensitively()
    {
        var ids = DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: true,
            userInstalled: ["WASM-tools"],
            sudoContextAttempted: true,
            sudoContextSucceeded: true,
            sudoInstalled: ["wasm-tools"],
            failingCommand: "dotnet workload list --machine-readable");

        Assert.Single(ids);
    }

    [Fact]
    public void CombineInstalledWorkloads_WindowsSkipsSudo_ReturnsUserList()
    {
        // On Windows the sudo probe is never attempted; combining must still
        // return the user list rather than throwing.
        var ids = DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: true,
            userInstalled: ["wasm-tools"],
            sudoContextAttempted: false,
            sudoContextSucceeded: false,
            sudoInstalled: null,
            failingCommand: "dotnet workload list --machine-readable");

        Assert.Equal(["wasm-tools"], ids);
    }

    [Fact]
    public void CombineInstalledWorkloads_BothProbesFail_ThrowsWithCommand()
    {
        var ex = Assert.Throws<Exception>(() => DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: false,
            userInstalled: null,
            sudoContextAttempted: true,
            sudoContextSucceeded: false,
            sudoInstalled: null,
            failingCommand: "dotnet workload list --machine-readable"));

        Assert.Contains("Workload command failed", ex.Message);
        Assert.Contains("dotnet workload list --machine-readable", ex.Message);
    }

    [Fact]
    public void CombineInstalledWorkloads_UserFailsOnWindows_Throws()
    {
        // Windows has no sudo fallback; if the user-context call fails, the
        // method must throw rather than silently returning an empty list.
        Assert.Throws<Exception>(() => DotNetWorkloadManager.CombineInstalledWorkloads(
            userContextSucceeded: false,
            userInstalled: null,
            sudoContextAttempted: false,
            sudoContextSucceeded: false,
            sudoInstalled: null,
            failingCommand: "dotnet workload list --machine-readable"));
    }

    [Fact]
    public async Task EnsureSudoCredentialsCachedAsync_NonInteractive_DoesNotBlockOnConsole()
    {
        var previous = DotNetCheck.Util.NonInteractive;
        try
        {
            DotNetCheck.Util.NonInteractive = true;

            // In non-interactive mode the helper must never invoke `sudo -v`
            // (which would block on /dev/tty). Result depends on whether the
            // `sudo -n true` probe finds cached creds on the runner — the
            // assertion here is only that the call returns within the timeout.
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await DotNetCheck.Util.EnsureSudoCredentialsCachedAsync(cts.Token);
        }
        finally
        {
            DotNetCheck.Util.NonInteractive = previous;
        }
    }
}
