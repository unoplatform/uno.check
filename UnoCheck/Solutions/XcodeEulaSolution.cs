using DotNetCheck.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	internal class XcodeEulaSolution : Solution
	{
		public override async Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			await base.Implement(sharedState, cancellationToken);

			_ = await Util.WrapShellCommandWithSudo("xcodebuild", new[] { "-license", "accept" });
		}
	}
}