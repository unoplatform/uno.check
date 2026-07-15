using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	/// <summary>
	/// Runs <c>dotnet workload update</c> with an explicit muxer so the update targets the
	/// intended .NET root — on multi-root machines a bare <c>dotnet</c> would hit whatever PATH
	/// resolves first, which is precisely the failure mode this solution repairs (see
	/// <see cref="Checkups.DotNetTargetingPackAlignmentCheckup"/>).
	/// </summary>
	public class DotNetWorkloadUpdateSolution : Solution
	{
		private readonly string _dotnetExePath;

		public DotNetWorkloadUpdateSolution(string dotnetExePath)
		{
			_dotnetExePath = dotnetExePath;
		}

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus($"Running '{_dotnetExePath} workload update'...");

			var result = await Task.Run(
				() => new ShellProcessRunner(new ShellProcessRunnerOptions(_dotnetExePath, "workload update", cancellationToken) { Verbose = true }).WaitForExit(),
				cancellationToken);

			if (result.Success)
			{
				ReportStatus("Workload manifests updated.");
			}
			else
			{
				ReportStatus($"'workload update' exited with code {result.ExitCode}.");
			}
		}
	}
}
