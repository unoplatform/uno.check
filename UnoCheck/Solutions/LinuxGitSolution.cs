using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	internal class LinuxGitSolution : Solution
	{
		public override async Task Implement(SharedState state, CancellationToken ct)
		{
			// One elevated invocation for the whole fix: update && install as a single
			// shell chain, so the graphical authorization flow (pkexec) shows one dialog
			// instead of one per apt command.
			await Util.WrapShellCommandWithSudo(
				"/bin/sh",
				workingDir: null,
				verbose: true,
				new[] { "-c", "apt-get update && apt-get install -y git" });
		}
	}
}
