using DotNetCheck.Models;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class LinuxNinjaOpenUrlSolution : Solution
	{
		/// <summary>Only opens a web page in the user's session; elevating would break it.</summary>
		public override bool RequiresElevation => false;

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