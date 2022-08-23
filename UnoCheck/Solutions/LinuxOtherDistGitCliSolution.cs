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

			var r = await Util.WrapShellCommandWithSudo("x-www-browser", new[] { GitOpenUrl });

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