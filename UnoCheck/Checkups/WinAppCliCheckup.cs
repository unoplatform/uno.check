#nullable enable

using System;
using System.ComponentModel;
using System.Threading.Tasks;

using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace DotNetCheck.Checkups
{
	internal class WinAppCliCheckup : Checkup
	{
		private const string SuggestionMessage = "WinApp CLI is not installed. To learn more visit https://devblogs.microsoft.com/ifdef-windows/introducing-dotnet-new-templates-for-winui/";

		// Starting a process whose executable is not on PATH throws out of Process.Start
		// before any exit code exists, so the machine this checkup is written for - a fresh
		// one without the CLI - never reaches a result to inspect. Only these two codes mean
		// "no such executable"; any other start failure is left to propagate so it is
		// reported as itself instead of being mistaken for a missing CLI.
		private const int ErrorFileNotFound = 2;
		private const int ErrorPathNotFound = 3;

		private readonly Func<Task<ShellProcessRunner.ShellProcessResult>> probeVersion;

		public WinAppCliCheckup()
			: this(() => new ShellProcessRunner(new("winapp", "--version") { Verbose = true }).WaitForExitAsync())
		{
		}

		internal WinAppCliCheckup(Func<Task<ShellProcessRunner.ShellProcessResult>> probeVersion)
			=> this.probeVersion = probeVersion;

		public override string Id => "winappcli";

		public override string Title => "WinApp CLI";

		public override bool IsPlatformSupported(Platform platform) => platform == Platform.Windows;

		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.VSWin != null;

		public override async Task<DiagnosticResult> Examine(SharedState history)
		{
			ShellProcessRunner.ShellProcessResult result;

			try
			{
				result = await probeVersion();
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode is ErrorFileNotFound or ErrorPathNotFound)
			{
				return NotInstalled();
			}

			if (!result.Success)
			{
				return NotInstalled();
			}

			var version = string.Join(" ", result.StandardOutput).Trim();

			ReportStatus(
				string.IsNullOrEmpty(version)
					? "WinApp CLI is installed."
					: $"WinApp CLI {version} is installed.",
				Status.Ok);

			return DiagnosticResult.Ok(this);
		}

		private DiagnosticResult NotInstalled()
			=> new(
				Status.Error,
				this,
				"WinApp CLI is not installed.",
				new Suggestion(SuggestionMessage, new WinAppCliInstallSolution()));
	}
}
