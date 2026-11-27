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

    [Fact]
    public void WindowsArguments_RunTheScriptAsAFile_SoAPathWithASpaceStaysOneArgument()
    {
        var args = DotNetSdkScriptInstallSolution.WindowsArguments(@"C:\Users\some one\AppData\Local\Temp\dotnet-install.ps1", @"C:\Program Files\dotnet", "10.0.100");

        Assert.Equal(
            ["-NoProfile", "-NonInteractive", "-File", "\"C:\\Users\\some one\\AppData\\Local\\Temp\\dotnet-install.ps1\"", "-InstallDir", "\"C:\\Program Files\\dotnet\"", "-Version", "10.0.100"],
            args);
    }
}
