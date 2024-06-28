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
				return Task.FromResult(new DiagnosticResult(Status.Error, this, "Failed to get PowerShell execution policy."));
			}
		}
	}
}