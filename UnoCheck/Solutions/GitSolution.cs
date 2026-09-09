using System;
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
		/// <summary>
		/// The installer that ships git is a reboot-capable Windows installer: 3010 means the
		/// work succeeded and a restart completes it, so it is not a failure of the fix.
		/// </summary>
		const int RebootRequired = 3010;

		[SuppressMessage("Interoperability", "CA1416:Validate platform compatibility")]
		public override Task Implement(SharedState state, CancellationToken ct)
		{
			var vsSetupKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\Setup", false);

			var sharedPath = vsSetupKey?.GetValue("SharedInstallationPath")?.ToString();

			if(sharedPath != null)
			{
				var vsInstaller = Path.Combine(Path.GetDirectoryName(sharedPath), "Installer", "setup.exe");

				if(File.Exists(vsInstaller))
				{
					var result = new ShellProcessRunner(new(vsInstaller, string.Empty)).WaitForExit();

					// Dismissing the installer leaves git missing; the fix has to say so rather
					// than let the runner report it as applied.
					if (result.ExitCode != 0 && result.ExitCode != RebootRequired)
						throw new InvalidOperationException(Util.BuildCommandFailureMessage("The installer", result));

					return Task.CompletedTask;
				}
			}

			// Nothing was attempted, so this cannot be reported as an applied fix.
			ReportStatus("Couldn't locate Visual Studio Installer.");

			throw new InvalidOperationException(
				"Couldn't locate the Visual Studio Installer, so git could not be installed automatically. Install git manually from https://git-scm.com/downloads.");
		}
	}
}
