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
}
