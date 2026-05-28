using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class WinAppCliInstallSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus("Installing WinApp CLI via winget...");

			var result = new ShellProcessRunner(new(
				"winget",
				"install --id Microsoft.WinAppCli -e --accept-source-agreements --accept-package-agreements",
				cancellationToken)
			{ Verbose = true }).WaitForExit();

			if (result.Success)
			{
				ReportStatus("WinApp CLI was installed.");
			}
			else
			{
				ReportStatus(
					"Failed to install WinApp CLI via winget. " +
					"Please install it manually with 'winget install Microsoft.WinAppCli' or 'npm install -g @microsoft/winappcli'.");
			}
		}
	}
}
