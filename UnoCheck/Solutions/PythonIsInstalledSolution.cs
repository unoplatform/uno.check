using DotNetCheck.Models;
using Microsoft.Win32;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class PythonIsInstalledSolution : Solution
	{
		public PythonIsInstalledSolution()
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

			ReportStatus("The Python version can be downloaded from Windows Store.");
		}
		
		private const string PythonDownloadUrl = "https://www.microsoft.com/store/productId/9PJPW5LDXLZ5";
	}
}