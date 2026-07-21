using System;
using System.Collections.Generic;
using System.Linq;
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

			// The muxer path is fully resolved, so invoke it directly instead of going
			// through the system shell (temp script + zsh/bash on unix): no quoting or
			// injection surface, same output capture.
			var result = await Task.Run(
				() => new ShellProcessRunner(new ShellProcessRunnerOptions(_dotnetExePath, "workload update", cancellationToken) { Verbose = true, UseSystemShell = false }).WaitForExit(),
				cancellationToken);

			if (result.Success)
			{
				ReportStatus("Workload manifests updated.");
			}
			else
			{
				// Throwing lets the fix runner surface the remediation failure instead of
				// reporting "Fix applied" for an update that did not happen.
				throw new Exception(BuildFailureMessage(_dotnetExePath, result.ExitCode, result.StandardOutput.Concat(result.StandardError)));
			}
		}

		/// <summary>
		/// The actionable reason (permissions, corrupted manifests, missing SDK band, ...)
		/// lives in the process output, so the failure message carries its tail alongside
		/// the exit code.
		/// </summary>
		internal static string BuildFailureMessage(string dotnetExePath, int exitCode, IEnumerable<string> outputLines)
		{
			var tail = string.Join(Environment.NewLine,
				outputLines.Where(line => !string.IsNullOrWhiteSpace(line)).TakeLast(10));

			return $"'{dotnetExePath} workload update' exited with code {exitCode}."
				+ (tail.Length == 0 ? string.Empty : $" Output:{Environment.NewLine}{tail}");
		}
	}
}
