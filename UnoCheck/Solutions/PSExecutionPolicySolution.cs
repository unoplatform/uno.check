using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	internal class PSExecutionPolicySolution : Solution
	{
		/// <summary>Sets the policy for <c>-Scope CurrentUser</c> only — never the machine scope.</summary>
		public override bool RequiresElevation => false;

		public override Task Implement(SharedState state, CancellationToken ct)
		{
			// A policy locked by group policy makes Set-ExecutionPolicy exit non-zero; the fix
			// has to report that rather than claim the policy was changed.
			Util.ThrowIfFailed(
				ShellProcessRunner.Run("powershell", "-Command Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned"),
				"Setting the PowerShell execution policy for the current user");

			return Task.CompletedTask;
		}
	}
}
