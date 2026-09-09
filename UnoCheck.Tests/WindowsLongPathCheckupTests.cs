using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.Models;
using Microsoft.Win32;

namespace UnoCheck.Tests;

/// <summary>
/// The checkup used to open its registry key for writing just to read one value, so every
/// unelevated run — which is every run from a host that cannot elevate itself — failed with
/// "Requested registry access is not allowed". That surfaced as a hard error on a correctly
/// configured machine, and one with no fix attached, because the throw happened before any
/// suggestion could be built.
/// </summary>
public class WindowsLongPathCheckupTests
{
    [Fact]
    public void TheKeyIsReadableWithoutWriteAccess()
    {
        if (!Util.IsWindows)
        {
            return;
        }

        using var readOnly = Registry.LocalMachine.OpenSubKey(WindowsLongPathCheckup.FileSystemKeyPath);

        Assert.NotNull(readOnly);
    }

    [Fact]
    public async Task ExamineDoesNotFailForWantOfElevation()
    {
        if (!Util.IsWindows)
        {
            return;
        }

        var result = await new WindowsLongPathCheckup().Examine(new SharedState());

        // Whatever this machine's setting is, the answer is a real diagnosis rather than the
        // access error the writable handle used to raise.
        Assert.DoesNotContain("registry access", result.Message ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        // A disabled setting still offers its fix; the fix is what needs the write access.
        if (result.Status == Status.Error)
        {
            Assert.True(result.HasSuggestion);
            Assert.True(result.Suggestion.HasSolution);
        }
    }
}
