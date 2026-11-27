using System.Diagnostics;
using System.Text.Json;

namespace UnoCheck.Tests;

// The .NET host writes a servicing breadcrumb to %ProgramData% for every library its .deps.json marks serviceable;
// in a sandboxed host (Win32 app isolation) each new file is an access request.
public class DepsFileTests
{
    private static readonly DirectoryInfo TestOutput = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    private static readonly string Configuration = TestOutput.Parent!.Name;
    private static readonly string RepoRoot = Path.GetFullPath(Path.Combine(TestOutput.FullName, "..", "..", "..", ".."));

    [Fact]
    public void BuiltDepsFile_MarksNoLibraryServiceable()
    {
        var depsFile = Path.Combine(RepoRoot, "UnoCheck", "bin", Configuration, "net6.0", "UnoCheck.deps.json");

        Assert.Empty(ServiceableLibraries(depsFile));
    }

    // The tool package is built from the publish output, whose .deps.json is generated apart from the build's.
    [Fact]
    public void PublishedDepsFile_MarksNoLibraryServiceable()
    {
        var publishDir = Path.Combine(Path.GetTempPath(), "unocheck-deps-" + Guid.NewGuid().ToString("N"));
        try
        {
            var project = Path.Combine(RepoRoot, "UnoCheck", "UnoCheck.csproj");
            var psi = new ProcessStartInfo("dotnet", $"publish \"{project}\" -c {Configuration} -f net6.0 -o \"{publishDir}\" -v q -nologo")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = RepoRoot
            };

            using var process = Process.Start(psi)!;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, output + error.Result);

            Assert.Empty(ServiceableLibraries(Path.Combine(publishDir, "UnoCheck.deps.json")));
        }
        finally
        {
            if (Directory.Exists(publishDir))
            {
                Directory.Delete(publishDir, recursive: true);
            }
        }
    }

    private static List<string> ServiceableLibraries(string depsFile)
    {
        Assert.True(File.Exists(depsFile), depsFile);

        using var deps = JsonDocument.Parse(File.ReadAllText(depsFile));
        return deps.RootElement.GetProperty("libraries").EnumerateObject()
            .Where(library => library.Value.TryGetProperty("serviceable", out var flag) && flag.ValueKind == JsonValueKind.True)
            .Select(library => library.Name)
            .ToList();
    }
}
