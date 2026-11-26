using System.ComponentModel;
using System.Security;
using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.Cli;
using DotNetCheck.DotNet;
using DotNetCheck.Models;

namespace UnoCheck.Tests;

/// <summary>
/// A check that was refused access can't tell "not installed" from "couldn't look". It says it could not check,
/// as a warning with no fix, instead of reporting the tool missing (issue #647).
/// </summary>
public class AccessRefusedTests
{
    const string DeniedMessage = "Access to the path 'C:\\ProgramData\\dotnet\\workloads' is denied.";

    [Fact]
    public void IsAccessRefused_UnauthorizedAccess_IsRefused()
        => Assert.True(AccessRefused.IsAccessRefused(new UnauthorizedAccessException(DeniedMessage)));

    [Fact]
    public void IsAccessRefused_SecurityException_IsRefused()
        => Assert.True(AccessRefused.IsAccessRefused(new SecurityException("Requested registry access is not allowed.")));

    [Fact]
    public void IsAccessRefused_ProcessStartDenied_IsRefused()
        => Assert.True(AccessRefused.IsAccessRefused(new Win32Exception(5)));

    [Fact]
    public void IsAccessRefused_ProcessNotFound_IsNotRefused()
        => Assert.False(AccessRefused.IsAccessRefused(new Win32Exception(2)));

    [Fact]
    public void IsAccessRefused_WrappedInAnotherException_IsRefused()
    {
        var wrapped = new InvalidOperationException("The check failed.", new UnauthorizedAccessException(DeniedMessage));

        Assert.True(AccessRefused.IsAccessRefused(wrapped));
    }

    [Fact]
    public void IsAccessRefused_InsideAnAggregate_IsRefused()
    {
        var aggregate = new AggregateException(new IOException("Disk error."), new UnauthorizedAccessException(DeniedMessage));

        Assert.True(AccessRefused.IsAccessRefused(aggregate));
    }

    [Fact]
    public void IsAccessRefused_AnotherFailure_IsNotRefused()
        => Assert.False(AccessRefused.IsAccessRefused(new InvalidOperationException("Something else.")));

    [Fact]
    public void FindInOutput_ReturnsTheLineThatNamesTheRefusal()
    {
        string[] output =
        [
            "Workload list",
            "Unhandled exception: System.UnauthorizedAccessException: " + DeniedMessage,
            "   at System.IO.Strategies.FileStreamHelpers.ValidateHandle()",
        ];

        Assert.Equal(output[1], AccessRefused.FindInOutput(output));
    }

    [Fact]
    public void FindInOutput_PlainMessageWithoutTheExceptionName_IsFound()
    {
        string[] output = [DeniedMessage];

        Assert.Equal(DeniedMessage, AccessRefused.FindInOutput(output));
    }

    [Fact]
    public void FindInOutput_NoRefusal_IsNull()
        => Assert.Null(AccessRefused.FindInOutput(["Workload command failed.", "Exit code 1"]));

    [Fact]
    public void IsReadRefused_FolderRefusesToBeListed_IsRefused()
        => Assert.True(AccessRefused.IsReadRefused("folder", _ => throw new UnauthorizedAccessException(DeniedMessage)));

    [Fact]
    public void IsReadRefused_FolderDoesNotExist_IsNotRefused()
        => Assert.False(AccessRefused.IsReadRefused("folder", _ => throw new DirectoryNotFoundException()));

    [Fact]
    public void IsReadRefused_FolderCanBeListed_IsNotRefused()
        => Assert.False(AccessRefused.IsReadRefused("folder", _ => ["a", "b"]));

    [Fact]
    public void Result_IsAWarningWithNoFix_ThatSaysItCouldNotCheck()
    {
        var checkup = new GitCheckup();

        var result = AccessRefused.Result(checkup, DeniedMessage);

        Assert.Equal(Status.Warning, result.Status);
        Assert.False(result.HasSuggestion);
        Assert.Equal("Could not check: " + DeniedMessage, result.Message);
    }

    [Fact]
    public void DiagnoseFailure_AccessRefused_CouldNotCheck()
    {
        var checkup = new GitCheckup();

        var result = CheckCommand.DiagnoseFailure(checkup, new UnauthorizedAccessException(DeniedMessage));

        Assert.Equal(Status.Warning, result.Status);
        Assert.False(result.HasSuggestion);
        Assert.Equal("Could not check: " + DeniedMessage, result.Message);
    }

    [Fact]
    public void DiagnoseFailure_WrappedRefusal_NamesTheRefusalNotTheWrapper()
    {
        var checkup = new GitCheckup();
        var wrapped = new InvalidOperationException("The check failed.", new UnauthorizedAccessException(DeniedMessage));

        var result = CheckCommand.DiagnoseFailure(checkup, wrapped);

        Assert.Equal("Could not check: " + DeniedMessage, result.Message);
    }

    [Fact]
    public void DiagnoseFailure_AnotherFailure_StaysAnError()
    {
        var checkup = new GitCheckup();

        var result = CheckCommand.DiagnoseFailure(checkup, new InvalidOperationException("Something else."));

        Assert.Equal(Status.Error, result.Status);
        Assert.Equal("Something else.", result.Message);
    }

    [Fact]
    public void VisualStudio_NothingFoundAndSetupStateRefused_CouldNotCheck()
    {
        var checkup = new VisualStudioWindowsCheckup();

        var result = checkup.NothingFoundResult(setupStateReadRefused: true);

        Assert.Equal(Status.Warning, result.Status);
        Assert.False(result.HasSuggestion);
        Assert.StartsWith("Could not check:", result.Message);
        Assert.Contains(VisualStudioWindowsCheckup.SetupStateFolder, result.Message);
    }

    [Fact]
    public void VisualStudio_NothingFoundAndSetupStateReadable_IsNotInstalled()
    {
        var checkup = new VisualStudioWindowsCheckup();

        var result = checkup.NothingFoundResult(setupStateReadRefused: false);

        Assert.Equal(Status.Warning, result.Status);
        Assert.Null(result.Message);
    }

    [Fact]
    public void VisualStudioWorkloads_MissingAndSetupStateRefused_CouldNotCheck()
    {
        var checkup = new VSWinWorkloadsCheckup();

        var result = checkup.MissingWorkloadsResult(setupStateReadRefused: true);

        Assert.Equal(Status.Warning, result.Status);
        Assert.False(result.HasSuggestion);
        Assert.StartsWith("Could not check:", result.Message);
    }

    [Fact]
    public void VisualStudioWorkloads_MissingAndSetupStateReadable_AsksToInstallThem()
    {
        var checkup = new VSWinWorkloadsCheckup();

        var result = checkup.MissingWorkloadsResult(setupStateReadRefused: false);

        Assert.Equal(Status.Error, result.Status);
        Assert.True(result.HasSuggestion);
    }

    [Fact]
    public void WorkloadListFailure_OutputNamesARefusal_ThrowsAccessRefused()
    {
        string[] output = ["Unhandled exception: System.UnauthorizedAccessException: " + DeniedMessage];

        var thrown = Assert.Throws<UnauthorizedAccessException>(
            () => DotNetWorkloadManager.ThrowWorkloadCommandFailed("dotnet workload list --machine-readable", output));

        Assert.Contains(DeniedMessage, thrown.Message);
    }

    [Fact]
    public void WorkloadListFailure_AnotherFailure_KeepsTheCommandFailedError()
    {
        var thrown = Assert.ThrowsAny<Exception>(
            () => DotNetWorkloadManager.ThrowWorkloadCommandFailed("dotnet workload list --machine-readable", ["Exit code 1"]));

        Assert.IsNotType<UnauthorizedAccessException>(thrown);
        Assert.Equal("Workload command failed: `dotnet workload list --machine-readable`", thrown.Message);
    }

    [Fact]
    public void Result_IsMarkedAsNotChecked()
        => Assert.True(AccessRefused.Result(new GitCheckup(), DeniedMessage).CouldNotCheck);

    [Fact]
    public void OrdinaryResult_IsNotMarkedAsNotChecked()
        => Assert.False(new DiagnosticResult(Status.Warning, new GitCheckup(), "Something to look at.").CouldNotCheck);

    [Theory]
    [InlineData(null, false, true)]
    [InlineData(Status.Error, false, true)]
    [InlineData(Status.Warning, true, true)]
    [InlineData(Status.Warning, false, false)]
    [InlineData(Status.Ok, false, false)]
    public void DependencyBlocksDependents_WhenItFailedWasNotRunOrCouldNotBeChecked(Status? status, bool couldNotCheck, bool expected)
        => Assert.Equal(expected, CheckCommand.DependencyBlocksDependents(status, couldNotCheck));
}
