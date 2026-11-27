using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups
{
	internal class PSExecutionPolicyCheckup : Checkup
	{
		public override string Id => "psexecpolicy";

		public override string Title => $"PowerShell Execution Policy";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override Task<DiagnosticResult> Examine(SharedState state)
		{
			// See: https://github.com/PowerShell/PowerShell/issues/18530#issuecomment-1325691850
			Environment.SetEnvironmentVariable("PSModulePath", string.Empty);

			var result = ShellProcessRunner.Run("powershell", "-Command Get-ExecutionPolicy -Scope CurrentUser");

			if (result.Success)
			{
				if (result.StandardOutput.Count != 0 && result.StandardOutput[0] == "RemoteSigned")
				{
					return Task.FromResult(DiagnosticResult.Ok(this));
				}
				else
				{
					return Task.FromResult(
						new DiagnosticResult(
							Status.Error,
							this,
							"PowerShell execution policy for current user is not RemoteSigned.",
							new("Change PowerShell execution policy to RemoteSigned", new PSExecutionPolicySolution())));
				}
			}
			else
			{
				Util.Log(FailureDetails(result.ExitCode, result.StandardOutput, result.StandardError));
				return Task.FromResult(FailedResult(result.StandardError));
			}
		}

		// What powershell printed, for the log: the verdict alone can't say why it failed.
		internal static string FailureDetails(int exitCode, IEnumerable<string> outputLines, IEnumerable<string> errorLines)
			=> string.Join(
				Environment.NewLine,
				new[] { $"PowerShell execution policy: powershell exited with code {exitCode}.", "stdout:" }
					.Concat(outputLines ?? Enumerable.Empty<string>())
					.Append("stderr:")
					.Concat(errorLines ?? Enumerable.Empty<string>()));

		internal DiagnosticResult FailedResult(IEnumerable<string> errorLines)
			=> AccessRefused.FindInOutput(errorLines) is { } refusal
				? AccessRefused.Result(this, refusal)
				: new DiagnosticResult(Status.Error, this, "Failed to get PowerShell execution policy.");
	}
}