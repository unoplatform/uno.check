using System.Threading;
using System.Threading.Tasks;

using DotNetCheck.Models;

namespace DotNetCheck.Solutions
{
	internal class PSExecutionPolicySolution : Solution
	{
		public override Task Implement(SharedState state, CancellationToken ct)
		{
			ShellProcessRunner.Run("powershell", "-Command Set-ExecutionPolicy -Scope CurrentUser -ExecutionPolicy RemoteSigned");

			return Task.CompletedTask;
		}
	}
}