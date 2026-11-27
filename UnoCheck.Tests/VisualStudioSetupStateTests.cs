using DotNetCheck.Checkups;

namespace UnoCheck.Tests;

/// <summary>Both Visual Studio checks share one probe of the setup folder: a sandboxed host asks the user for each read.</summary>
public class VisualStudioSetupStateTests
{
    [Fact]
    public void SetupStateProbe_AskedTwice_ReadsTheFolderOnce()
    {
        var reads = 0;
        var isRefused = VisualStudioWindowsCheckup.SetupStateProbe(folder =>
        {
            reads++;
            throw new UnauthorizedAccessException($"Access to the path '{folder}' is denied.");
        });

        Assert.True(isRefused());
        Assert.True(isRefused());
        Assert.Equal(1, reads);
    }

    [Fact]
    public void SetupStateProbe_ReadsTheSetupStateFolder()
    {
        string? read = null;
        var isRefused = VisualStudioWindowsCheckup.SetupStateProbe(folder =>
        {
            read = folder;
            return [];
        });

        Assert.False(isRefused());
        Assert.Equal(VisualStudioWindowsCheckup.SetupStateFolder, read);
    }
}
