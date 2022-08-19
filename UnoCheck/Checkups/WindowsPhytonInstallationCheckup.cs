#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Microsoft.Win32;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class WindowsPhytonInstallationCheckup : Checkup
	{
		public override string Id => "windowsphytonInstallation";

		public override string Title => "Windows Phyton Installation Checkup";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			RegistryKey pyLaucherKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Python\PyLauncher", false);

			if (pyLaucherKey is null || string.IsNullOrEmpty(pyLaucherKey.GetValue(InstallDirKey)?.ToString()))
			{
				return await Task.FromResult(new DiagnosticResult(
				Status.Error,
				this,
				new Suggestion("To setup your machine to use AOT modes on Windows, you will need to install Python from Windows Store, or manually through Python's official site",
				new PytonIsInstalledSolution())));
			}
			else
			{
				ReportStatus($"Python is installed, you can use WebAssembly AOT modules on Windows!", Status.Ok);
			}

			return await Task.FromResult(DiagnosticResult.Ok(this));
		}

		private const string InstallDirKey = "InstallDir";
	}
}