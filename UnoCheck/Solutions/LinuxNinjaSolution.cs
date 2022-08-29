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

			_ = await Util.WrapShellCommandWithSudo("apt-get", new[] { "update" });
			_ = await Util.WrapShellCommandWithSudo("apt-get", new[] { "install", "ninja-build" });

			ReportStatus("Ninja Build System was installed on Linux.");
		}
	}
}