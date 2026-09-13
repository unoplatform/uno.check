using System.Diagnostics;

namespace UnoCheck.Tests;

public class LaunchSettingsTests
{
    [Fact]
    public void Ensure_LaunchSettings_File_Is_Valid()
    {
        var repoRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var launchSettings = Path.Combine(repoRoot, "UnoCheck", "Properties", "launchSettings.json");
        Assert.True(File.Exists(launchSettings));

        var projectPath = Path.Combine(repoRoot, "UnoCheck", "UnoCheck.csproj");

        // UseAppHost=false runs the project through the dotnet host (`dotnet UnoCheck.dll`)
        // instead of the apphost exe, whose manifest requests requireAdministrator and
        // cannot start from a non-elevated test run (Win32 error 740).
        var psi = new ProcessStartInfo("dotnet", $"run --project \"{projectPath}\" --framework net6.0 -p:UseAppHost=false --launch-profile UnoCheck -- --non-interactive --ci")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };

        using var process = Process.Start(psi)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        Assert.True(string.IsNullOrEmpty(error), error);
        Assert.DoesNotContain("Error: Could not find a part of the path", output);
    }
}