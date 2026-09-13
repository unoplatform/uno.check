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
			// The Windows directory comes from the OS rather than from %windir%: this path is
			// executed, and under a host that cannot elevate itself it is run through an
			// elevated child — so an environment block that redirected %windir% would be
			// choosing which binary runs as administrator.
			var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

			// For 32-bit processes on 64-bit systems, the system32 folder can only be reached
			// through sysnative.
			var systemDirectory = Environment.Is64BitOperatingSystem && !Environment.Is64BitProcess
				? "sysnative"
				: "system32";

			var dismPath = Path.Combine(windowsDirectory, systemDirectory, "dism.exe");

			return ShellProcessRunner.Run(dismPath, "/Online /Enable-Feature /Quiet /NoRestart /All /FeatureName:Microsoft-Hyper-V");
		}
	}
}
