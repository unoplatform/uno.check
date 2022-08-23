using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	internal class MacGitCliSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			_ = await Util.WrapShellCommandWithSudo("git", new[] { "--version" });

			ReportStatus("WThe Uno-check will prompt you about the installation.");
		}
	}
}