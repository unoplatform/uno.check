using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	internal class LinuxGitCliSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);
			
			var r = await Util.WrapShellCommandWithSudo("apt-get", new[] { "install", "git" });

			ReportStatus(r.GetOutput());
		}
	}
}