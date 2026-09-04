using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

public class DotNetSdkScriptInstallSolutionTests
{
    [Fact]
    public void IsDirectoryWritableOrCreatable_UsesNearestExistingParent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "uno-check-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var nestedPath = Path.Combine(tempDir, "not-created", "dotnet");

            Assert.True(DotNetSdkScriptInstallSolution.IsDirectoryWritableOrCreatable(nestedPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void IsDirectoryWritableOrCreatable_MissingPath_ReturnsFalse()
    {
        Assert.False(DotNetSdkScriptInstallSolution.IsDirectoryWritableOrCreatable(string.Empty));
    }
}
