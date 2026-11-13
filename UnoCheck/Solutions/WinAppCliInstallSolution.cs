#nullable enable

using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	public class WinAppCliInstallSolution : Solution
	{
		private const string ManualInstallMessage =
			"Failed to install WinApp CLI via winget. " +
			"Please install it manually with 'winget install Microsoft.WinAppCli' or 'npm install -g @microsoft/winappcli'.";

		// winget is absent on older Windows builds and on Windows Server images. Without
		// this, Process.Start throws and the fix reports the raw "cannot find the file"
		// error instead of the manual instructions the user can act on. See the matching
		// codes in WinAppCliCheckup.
		private const int ErrorFileNotFound = 2;
		private const int ErrorPathNotFound = 3;

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus("Installing WinApp CLI via winget...");

			ShellProcessRunner.ShellProcessResult result;

			try
			{
				result = await new ShellProcessRunner(new(
					"winget",
					"install --id Microsoft.WinAppCli -e --accept-source-agreements --accept-package-agreements",
					cancellationToken)
				{ Verbose = true }).WaitForExitAsync();
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode is ErrorFileNotFound or ErrorPathNotFound)
			{
				ReportStatus(ManualInstallMessage);
				return;
			}

			ReportStatus(result.Success ? "WinApp CLI was installed." : ManualInstallMessage);
		}
	}
}
