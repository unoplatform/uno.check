#nullable enable

using DotNetCheck.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Check if Windows Subsystem for Linux is installed.
	/// </summary>
	public class WSLCheckup : Checkup
	{
		public override bool IsPlatformSupported(Platform platform)
			=> platform == Platform.Windows;

		public override string Id => "wsl";

		public override string Title => "Windows Subsystem for Linux";

		public override Task<DiagnosticResult> Examine(SharedState history)
		{
			var distros = GetAvailableDistros();

			if (distros?.Length > 0)
			{
				foreach (var distro in distros)
				{
					ReportStatus(distro, Status.Ok);

				}

				return Task.FromResult(DiagnosticResult.Ok(this));
			}
			else
			{
				ReportStatus(@"WSL not available.", Status.Warning);

				var result = new DiagnosticResult(
					Status.Warning,
					this,
					prescription: new Suggestion("Install WSL", @"To test Linux applications from this machine, follow these instructions to install and configure Windows Subsystem for Linux: https://platform.uno/docs/articles/get-started-with-linux.html")
				);
				return Task.FromResult(result);
			}
		}

		private string[]? GetAvailableDistros()
		{
			try
			{
				var process = new ShellProcessRunner(new ShellProcessRunnerOptions("wsl", "-l") { OutputEncoding = Encoding.Unicode });
				var result = process.WaitForExit();
				return result.StandardOutput
					.Where(s => !string.IsNullOrWhiteSpace(s))
					.Skip(1)
					.ToArray();
			}
			catch
			{
				return null;
			}
		}
	}
}
