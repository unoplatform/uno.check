using DotNetCheck.Models;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LinuxNinjaOpenUrlSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			var ps = new ProcessStartInfo(LinuxNinjaOrgUrl)
			{
				UseShellExecute = true,
				Verb = "open"
			};
			_ = Process.Start(ps);

			ReportStatus($"To install ninja, please visit {LinuxNinjaOrgUrl}");
		}

		private const string LinuxNinjaOrgUrl = "https://ninja-build.org/";
	}
}