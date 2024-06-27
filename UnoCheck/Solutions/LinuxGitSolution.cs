using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	internal class LinuxGitSolution : Solution
	{
		public override async Task Implement(SharedState state, CancellationToken ct)
		{
			await Util.WrapShellCommandWithSudo("apt", workingDir: null, verbose: true, new[] { "update" });

			await Util.WrapShellCommandWithSudo("apt", workingDir: null, verbose: true, new[] { "install", "git" });
		}
	}
}