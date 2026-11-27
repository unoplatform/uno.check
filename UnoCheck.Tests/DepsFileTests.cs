using System.Diagnostics;
using System.Text.Json;

namespace UnoCheck.Tests;

// Why no library is serviceable: see the MarkBuildDepsFileNotServiceable comment in UnoCheck/UnoCheck.csproj.
public class DepsFileTests
{
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(5);
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
            var psi = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                WorkingDirectory = RepoRoot
            };
            // No node reuse or compiler server: a process outliving the publish would hold the redirected pipes open.
            foreach (var argument in new[] { "publish", project, "-c", Configuration, "-f", "net6.0", "-o", publishDir, "-v", "q", "-nologo", "-nr:false", "-p:UseSharedCompilation=false" })
            {
                psi.ArgumentList.Add(argument);
            }

            foreach (var inherited in new[] { "MSBuildSDKsPath", "MSBUILD_EXE_PATH", "MSBuildExtensionsPath" })
            {
                psi.Environment.Remove(inherited);
            }

            using var process = Process.Start(psi)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(PublishTimeout))
            {
                process.Kill(entireProcessTree: true);
                Assert.Fail($"dotnet publish did not finish within {PublishTimeout}.");
            }

            Assert.True(process.ExitCode == 0, output.Result + error.Result);

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
