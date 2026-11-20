#nullable enable

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text.RegularExpressions;
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

		private static readonly Regex VersionLine = new(@"^\d+(\.\d+)+(-[0-9A-Za-z.-]+)?$", RegexOptions.Compiled);

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

		// Same gate as EdgeWebView2Checkup, CI included. A build agent does not need the
		// CLI to compile, and leaving it on would make every Windows CI run reach out to
		// winget for a network install - one of which already came back 500 and failed the
		// job. Developers, who are who the check is for, still get it.
		public override bool ShouldExamine(SharedState history)
			=> Manifest?.Check?.VSWin != null
				&& !history.GetEnvironmentVariableFlagSet("CI");

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

			var version = ExtractVersion(result.StandardOutput);

			ReportStatus(
				string.IsNullOrEmpty(version)
					? "WinApp CLI is installed."
					: $"WinApp CLI {version} is installed.",
				Status.Ok);

			return DiagnosticResult.Ok(this);
		}

		/// <summary>
		/// <c>--version</c> prints the version on a line of its own, but the CLI's first run
		/// prefixes the same stream with an ASCII-art banner and a telemetry notice. Joining
		/// every line put all of that into the status message, so take the last line that is
		/// a version and nothing else.
		/// </summary>
		private static string? ExtractVersion(IReadOnlyList<string> standardOutput)
		{
			for (var i = standardOutput.Count - 1; i >= 0; i--)
			{
				var candidate = standardOutput[i]?.Trim();

				if (!string.IsNullOrEmpty(candidate) && VersionLine.IsMatch(candidate))
					return candidate;
			}

			return null;
		}

		private DiagnosticResult NotInstalled()
			=> new(
				Status.Error,
				this,
				"WinApp CLI is not installed.",
				new Suggestion(SuggestionMessage, new WinAppCliInstallSolution()));
	}
}
