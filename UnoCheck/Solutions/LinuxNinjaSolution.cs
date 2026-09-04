using DotNetCheck.Models;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LinuxNinjaSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			// One elevated invocation for the whole fix: update && install as a single
			// shell chain, so the graphical authorization flow (pkexec) shows one dialog
			// instead of one per apt command.
			_ = await Util.WrapShellCommandWithSudo(
				"/bin/sh",
				new[] { "-c", "apt-get update && apt-get install -y ninja-build" });

			ReportStatus("Ninja Build System was installed on Linux.");
		}
	}
}
