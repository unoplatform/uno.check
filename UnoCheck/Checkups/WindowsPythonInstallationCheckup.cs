
#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;
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
			RegistryKey pyLaucherKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Python\PyLauncher", false);
			string? path = pyLaucherKey?.GetValue(InstallDirKey)?.ToString();

			if (pyLaucherKey is null || string.IsNullOrEmpty(path))
			{
				return await Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("In order to build WebAssembly apps using AOT, you will need to install Python from Windows Store, or manually through Python's official site",
				new PytonIsInstalledSolution())));
			}
			else
			{
				ReportStatus($"Python is installed in {path}.", Status.Ok);
			}

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private const string InstallDirKey = "InstallDir";
	}
}