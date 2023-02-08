
#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
    public class WindowsPythonInstallationCheckup : Checkup
    {
        public override string Id => "windowspyhtonInstallation";

        public override string Title => "Windows Python Installation Checkup";

        public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

        public override async Task<DiagnosticResult> Examine(SharedState history)
        {
            var checkResult = RegistryCheck();
            if (!checkResult.available)
            {
                // In case of Microsoft Store version, the registry key is not set.
                // Fall back to where command.
                checkResult = WhereCheck();
            }

            if (!checkResult.available)
            {
                return await Task.FromResult(new DiagnosticResult(
                Status.Error,
                this,
                new Suggestion("In order to build WebAssembly apps using AOT, you will need to install Python from Microsoft Store, winget, or manually through Python's official site",
                new PythonIsInstalledSolution())));
            }
            else
            {
                ReportStatus($"Python is installed in {checkResult.path}.", Status.Ok);
            }

            return await Task.FromResult(DiagnosticResult.Ok(this));
        }

        private const string InstallDirKey = "InstallDir";

        private (bool available, string? path) RegistryCheck()
        {
            var pyLaucherKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Python\PyLauncher", false);
            string? path = pyLaucherKey?.GetValue(InstallDirKey)?.ToString();
            var available = pyLaucherKey is not null && !string.IsNullOrEmpty(path);
            return (available, path);
        }

        private (bool available, string? path) WhereCheck()
        {
            using Process process = new Process();
            process.StartInfo.FileName = "where";
            process.StartInfo.Arguments = "python";
            process.StartInfo.UseShellExecute = false;
            process.StartInfo.RedirectStandardOutput = true;
            process.Start();

            string output = process.StandardOutput.ReadToEnd();
            return (!string.IsNullOrEmpty(output), output);
        }
    }
}