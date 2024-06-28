using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;
using Microsoft.Win32;

namespace DotNetCheck.Solutions
{
	internal class GitSolution : Solution
	{
		[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
		public override Task Implement(SharedState state, CancellationToken ct)
		{
			var vsSetupKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\Setup", false);

			var sharedPath = vsSetupKey.GetValue("SharedInstallationPath")?.ToString();

			if(sharedPath != null)
			{
				var vsInstaller = Path.Combine(Path.GetDirectoryName(sharedPath), "Installer", "setup.exe");

				if(File.Exists(vsInstaller))
				{
					new ShellProcessRunner(new(vsInstaller, string.Empty)).WaitForExit();

					return Task.CompletedTask;
				}
			}

			ReportStatus("Couldn't locate Visual Studio Installer.");

			return Task.CompletedTask;
		}
	}
}