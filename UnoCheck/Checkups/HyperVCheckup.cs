#nullable enable

using DotNetCheck.Models;
using DotNetCheck.Solutions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	public class HyperVCheckup : Checkup
	{
		public override string Id => "windowshyperv";

		public override string Title => $"Windows Hyper-V Checkup";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			if (!this.GetHyperVStatus())
			{
				return Task.FromResult(new DiagnosticResult(
				Status.Warning,
				this,
				new Suggestion($"Activate Hyper-V on this computer to benefit from faster Android Emulators",
				new HyperVActivationSolution())));
			}
			else
			{
				ReportStatus($"Hyper-V is configured!", Status.Ok);

				return Task.FromResult(DiagnosticResult.Ok(this));
			}
		}

		private bool GetHyperVStatus()
		{
			var startInfo = new ProcessStartInfo
			{
				Arguments = "/enum {current}",
				CreateNoWindow = true,
				FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "bcdedit.exe"),
				RedirectStandardOutput = true,
				UseShellExecute = false
			};
			using (var process = Process.Start(startInfo))
			{
				process.WaitForExit();

				while (!process.StandardOutput.EndOfStream)
				{
					string? line = process.StandardOutput.ReadLine();

					if (!string.IsNullOrEmpty(line))
					{
						if (line.StartsWith("hypervisorlaunchtype ", StringComparison.OrdinalIgnoreCase))
						{
							return line.IndexOf(" off", StringComparison.OrdinalIgnoreCase) == -1;
						}
					}
				}
			}
			return false;
		}
	}
}