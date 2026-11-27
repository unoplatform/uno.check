using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>Both Visual Studio checks share one probe of the setup folder: a sandboxed host asks the user for each read.</summary>
public class VisualStudioSetupStateTests
{
    [Fact]
    public void SetupStateProbe_Refused_ReadsTheFolderOnce()
    {
        var reads = 0;
        var isRefused = VisualStudioWindowsCheckup.SetupStateProbe(folder =>
        {
            reads++;
            throw new UnauthorizedAccessException($"Access to the path '{folder}' is denied.");
        });

        Assert.True(isRefused.Value);
        Assert.True(isRefused.Value);
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SetupStateProbe_Readable_ReadsTheSetupStateFolderOnce()
    {
        var read = new List<string>();
        var isRefused = VisualStudioWindowsCheckup.SetupStateProbe(folder =>
        {
            read.Add(folder);
            return [];
        });

        Assert.False(isRefused.Value);
        Assert.False(isRefused.Value);
        Assert.Equal([VisualStudioWindowsCheckup.SetupStateFolder], read);
    }

    [Fact]
    public void IsSetupStateReadRefused_AnswersFromTheSharedProbe()
    {
        var answer = VisualStudioWindowsCheckup.IsSetupStateReadRefused();

        Assert.True(VisualStudioWindowsCheckup.SetupStateReadRefused.IsValueCreated);
        Assert.Equal(VisualStudioWindowsCheckup.SetupStateReadRefused.Value, answer);
    }

    static Lazy<bool> ProbeThatMustNotRun()
        => VisualStudioWindowsCheckup.SetupStateProbe(_ => throw new InvalidOperationException("The folder must not be read."));

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void IsSetupStateReadRefused_InAnAppContainer_FollowsVsWhereWithoutProbing(bool vsWhereFoundNoInstall)
    {
        var probe = ProbeThatMustNotRun();

        Assert.Equal(vsWhereFoundNoInstall, VisualStudioWindowsCheckup.IsSetupStateReadRefused(inAppContainer: true, () => vsWhereFoundNoInstall, probe));
        Assert.False(probe.IsValueCreated);
    }

    [Fact]
    public void IsSetupStateReadRefused_OutsideAnAppContainer_AnswersFromTheProbe()
    {
        var probe = VisualStudioWindowsCheckup.SetupStateProbe(folder => throw new UnauthorizedAccessException($"Access to the path '{folder}' is denied."));

        Assert.True(VisualStudioWindowsCheckup.IsSetupStateReadRefused(inAppContainer: false, () => throw new InvalidOperationException("vswhere must not be asked."), probe));
        Assert.True(probe.IsValueCreated);
    }

    // A missing vswhere read nothing, so its empty answer means "not installed", not "refused".
    [Fact]
    public void VsWhereFoundNoInstall_NoVsWhere_IsFalseWithoutListing()
        => Assert.False(VisualStudioWindowsCheckup.VsWhereFoundNoInstall(vsWherePath: null, () => throw new InvalidOperationException("Nothing to list.")));

    [Fact]
    public void VsWhereFoundNoInstall_AnInstallListed_IsFalse()
        => Assert.False(VisualStudioWindowsCheckup.VsWhereFoundNoInstall("vswhere.exe", () => [new VisualStudioInfo()]));

    [Fact]
    public void VsWhereFoundNoInstall_NothingListed_IsTrue()
        => Assert.True(VisualStudioWindowsCheckup.VsWhereFoundNoInstall("vswhere.exe", () => []));
}
