using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DotNetCheck
{
	/// <summary>
	/// Runs one command through the macOS system administrator authorization dialog.
	/// The containing Uno.Check process remains in the original user's context.
	/// </summary>
	internal static class MacOsAdministratorCommandRunner
	{
		const string OsascriptPath = "/usr/bin/osascript";
		const string RunHandler = "on run argv";
		const string ElevatedCommand = "do shell script (item 1 of argv) with administrator privileges with prompt \"Uno.Check needs administrator access to apply this fix.\"";
		const string EndHandler = "end run";

		public static ShellProcessRunner.ShellProcessResult Run(
			string executable,
			string workingDirectory,
			bool verbose,
			CancellationToken cancellationToken,
			IReadOnlyList<string> arguments)
		{
			if (!Util.IsMac)
				throw new PlatformNotSupportedException("The macOS administrator dialog is only available on macOS.");

			if (!File.Exists(OsascriptPath))
				throw new FileNotFoundException("The macOS administrator authorization tool was not found.", OsascriptPath);

			cancellationToken.ThrowIfCancellationRequested();

			var commandLine = BuildCommandLine(executable, arguments);
			var runner = new ShellProcessRunner(new ShellProcessRunnerOptions(OsascriptPath, string.Empty, cancellationToken)
			{
				ArgumentList = new[] { "-e", RunHandler, "-e", ElevatedCommand, "-e", EndHandler, "--", commandLine },
				WorkingDirectory = workingDirectory,
				Verbose = verbose,
				UseSystemShell = false,
				RedirectOutput = true,
			});

			var result = runner.WaitForExit();
			cancellationToken.ThrowIfCancellationRequested();

			if (WasDeclined(result))
			{
				return new ShellProcessRunner.ShellProcessResult(
					result.StandardOutput,
					new List<string> { "Administrator approval was declined." },
					result.ExitCode);
			}

			return result;
		}

		internal static string BuildCommandLine(string executable, IEnumerable<string> arguments)
		{
			if (string.IsNullOrWhiteSpace(executable))
				throw new ArgumentException("An executable is required.", nameof(executable));

			var command = new List<string> { PosixShellQuote(executable) };
			command.AddRange((arguments ?? Array.Empty<string>()).Select(PosixShellQuote));
			return string.Join(" ", command);
		}

		internal static string PosixShellQuote(string value)
			=> "'" + (value ?? string.Empty).Replace("'", "'\"'\"'") + "'";

		internal static bool WasDeclined(ShellProcessRunner.ShellProcessResult result)
		{
			if (result == null || result.Success)
				return false;

			var output = result.StandardOutput.Concat(result.StandardError);
			return output.Any(line =>
				line?.IndexOf("User canceled", StringComparison.OrdinalIgnoreCase) >= 0
				|| line?.IndexOf("User cancelled", StringComparison.OrdinalIgnoreCase) >= 0
				|| line?.IndexOf("(-128)", StringComparison.OrdinalIgnoreCase) >= 0);
		}
	}
}
