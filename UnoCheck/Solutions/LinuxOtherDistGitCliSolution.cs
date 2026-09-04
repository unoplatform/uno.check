using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LinuxOtherDistGitCliSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			// Opening a browser needs the user's session, not root: elevation wrappers
			// (sudo/pkexec) strip DISPLAY/WAYLAND_DISPLAY, so an elevated launch fails
			// after authentication and the prompt only confuses.
			var r = await Util.ShellCommand(
				"x-www-browser",
				workingDir: null,
				verbose: Util.Verbose,
				cancellationToken,
				new[] { GitOpenUrl });

			if (r.ExitCode == 0)
			{
				ReportStatus("For other Linux distributions, please check the Git Cli web page.");
			}
			else
			{
				ReportStatus($"For this Linux distribution, check the web browser and the instruction on {GitOpenUrl}.");
			}
		}

		private const string GitOpenUrl = "https://git-scm.com/book/en/v2/Getting-Started-Installing-Git";
	}
}
