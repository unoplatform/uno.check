using DotNetCheck.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class PytonIsInstalledSolution : Solution
	{
		public PytonIsInstalledSolution()
		{
		}

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			var ps = new ProcessStartInfo(PythonDownloadUrl)
			{
				UseShellExecute = true,
				Verb = "open"
			};
			_ = Process.Start(ps);

			ReportStatus("The Phyton version can be downloaded on this website..");
		}
		
		private const string PythonDownloadUrl = "https://www.python.org/downloads/";
	}
}