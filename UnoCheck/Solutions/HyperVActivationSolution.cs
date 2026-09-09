using DotNetCheck.Models;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DotNetCheck.Solutions
{
	public class HyperVActivationSolution : Solution
	{
		/// <summary>
		/// DISM's "the work succeeded, a restart completes it" code. Everything else non-zero
		/// is a real failure — access denied, an unavailable feature, a servicing error.
		/// </summary>
		private const int RebootRequired = 3010;

		public HyperVActivationSolution()
		{
		}

		public override Task Implement(SharedState sharedState, CancellationToken cancellationToken)
		{
			var dism = RunDism();

			if (dism.ExitCode == 0 || dism.ExitCode == RebootRequired)
			{
				// Enabling Hyper-V always needs a restart, whether DISM says 0 or 3010.
				ReportStatus("Hyper-V activated. Restart your computer to complete the change.");
				return Task.CompletedTask;
			}

			// Previously every non-zero code raised "please restart your computer", so a fix
			// that had failed outright — DISM refusing for want of rights, or the feature not
			// being available on this edition — told the user to reboot and discarded the only
			// output that said what actually went wrong.
			throw new InvalidOperationException(Util.BuildCommandFailureMessage("Enabling Hyper-V", dism));
		}

		private static ShellProcessRunner.ShellProcessResult RunDism()
		{
			var dismPath = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "system32", "dism.exe");

			if (Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess)
			{
				// For 32-bit processes on 64-bit systems, %windir%\system32 folder
				// can only be accessed by specifying %windir%\sysnative folder.
				dismPath = Path.Combine(Environment.ExpandEnvironmentVariables("%windir%"), "sysnative", "dism.exe");
			}

			return ShellProcessRunner.Run(dismPath, "/Online /Enable-Feature /Quiet /NoRestart /All /FeatureName:Microsoft-Hyper-V");
		}
	}
}
