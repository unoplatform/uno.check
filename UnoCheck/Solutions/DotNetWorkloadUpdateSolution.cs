using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.DotNet;
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

		/// <summary>
		/// Workload manifests are written under the .NET root that owns this muxer, so the
		/// answer follows that root's writability: a machine-wide SDK needs elevation, a
		/// user-local one does not.
		/// </summary>
		public override bool RequiresElevation
			=> !Util.IsDirectoryWritable(System.IO.Path.GetDirectoryName(_dotnetExePath) ?? _dotnetExePath);

		/// <summary>
		/// macOS/Linux elevate the command itself (sudo or the authorization dialog); Windows
		/// elevates the whole fix child from <see cref="RequiresElevation"/> instead.
		/// </summary>
		internal static bool ShouldRunElevated(bool isWindows, bool requiresElevation)
			=> !isWindows && requiresElevation;

		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			ReportStatus($"Running '{_dotnetExePath} workload update'...");

			// Both paths use the resolved muxer. Protected macOS/Linux roots use the
			// elevation helper, which quotes the command and arguments for its shell;
			// other roots run the muxer directly without a shell. Both capture output.
			var result = ShouldRunElevated(Util.IsWindows, RequiresElevation)
				? await DotNetWorkloadManager.RunWithSudoAsync(_dotnetExePath, workingDir: null, cancellationToken, new[] { "workload", "update" })
				: await Task.Run(
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
