using DotNetCheck.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class HyperVActivationSolution : Solution
	{
		public HyperVActivationSolution()
		{
		}

		public override Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			if (this.ActivateHyperV())
			{
				ReportStatus("Hyper-V activated. Please, restart your computer.");
			}
			else
			{
				throw new InvalidOperationException("Please, restart your computer to complete the Hyper-V activation process.");
			}

			return Task.CompletedTask;
		}

		private bool ActivateHyperV()
		{
			string dsimPath = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "dism.exe");

			if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
			{
				// For 32-bit processes on 64-bit systems, %windir%\system32 folder
				// can only be accessed by specifying %windir%\sysnative folder.
				dsimPath = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "sysnative", "dism.exe");
			}

			var dism = ShellProcessRunner.Run(dsimPath, "/Online /Enable-Feature /Quiet /NoRestart /All /FeatureName:Microsoft-Hyper-V");

			if(dism.ExitCode == RebootRequired)
			{
				ReportStatus("Please, restart your computer to complete the Hyper-V activation process.");

				return false;
			}

			return dism.ExitCode == 0;
		}

		private const int RebootRequired = 3010;
	}
}