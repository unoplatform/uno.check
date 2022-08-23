using DotNetCheck.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class GitCliSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			RegistryKey vsSetupKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\Setup", false);

			string? setupPath = vsSetupKey.GetValue(SharedInstallationPath)?.ToString();

			if(setupPath is not null)
			{
				string windowsInstallerExe = setupPath.Replace(SetupPath, InstallerPath);

				if(File.Exists(windowsInstallerExe))
				{
					var vs = new ProcessStartInfo(windowsInstallerExe)
					{
						UseShellExecute = true,
						Verb = "open"
					};
					_ = Process.Start(vs);

					ReportStatus("Please, install via Visual Studio Installer");
				}
				else
				{
					ReportStatus("Visual Studio Installer was not found.");

					var ps = new ProcessStartInfo(VisualStudioDownloadUrl)
					{
						UseShellExecute = true,
						Verb = "open"
					};
					_ = Process.Start(ps);
				}
			}
		}
		
		private const string SharedInstallationPath = "SharedInstallationPath";
		private const string SetupPath = @"\Setup";
		private const string InstallerPath = @"\Installer\setup.exe";
		private const string VisualStudioDownloadUrl = "https://visualstudio.microsoft.com/downloads/";

	}
}