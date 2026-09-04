using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace DotNetCheck
{
	/// <summary>
	/// Runs one command through the Linux polkit authorization dialog (pkexec).
	/// The containing Uno.Check process remains in the original user's context.
	/// pkexec receives the program and its arguments as a typed argument vector —
	/// no shell is involved, so argument boundaries are preserved as-is.
	/// </summary>
	internal static class LinuxAdministratorCommandRunner
	{
		static readonly string[] PkexecPaths = { "/usr/bin/pkexec", "/bin/pkexec" };

		public static ShellProcessRunner.ShellProcessResult Run(
			string executable,
			string workingDirectory,
			bool verbose,
			CancellationToken cancellationToken,
			IReadOnlyList<string> arguments)
		{
			if (!Util.IsLinux)
				throw new PlatformNotSupportedException("The polkit authorization dialog is only available on Linux.");

			var pkexec = PkexecPaths.FirstOrDefault(File.Exists)
				?? throw new FileNotFoundException(
					"The polkit authorization tool (pkexec) was not found. Install the polkit package for your distribution.",
					PkexecPaths[0]);

			cancellationToken.ThrowIfCancellationRequested();

			var runner = new ShellProcessRunner(new ShellProcessRunnerOptions(pkexec, string.Empty, cancellationToken)
			{
				ArgumentList = BuildArguments(executable, arguments),
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

		internal static IReadOnlyList<string> BuildArguments(string executable, IEnumerable<string> arguments)
		{
			if (string.IsNullOrWhiteSpace(executable))
				throw new ArgumentException("An executable is required.", nameof(executable));

			var argumentList = new List<string> { executable };
			argumentList.AddRange(arguments ?? Array.Empty<string>());
			return argumentList;
		}

		/// <summary>
		/// pkexec exits with 126 when the user dismisses the authentication dialog and
		/// writes "Request dismissed" to stderr; 127 covers authorization failures.
		/// Only the explicit dismissal is normalized — other failures keep their output.
		/// </summary>
		internal static bool WasDeclined(ShellProcessRunner.ShellProcessResult result)
		{
			if (result == null || result.Success)
				return false;

			if (result.ExitCode == 126)
				return true;

			var output = result.StandardOutput.Concat(result.StandardError);
			return output.Any(line =>
				line?.IndexOf("Request dismissed", StringComparison.OrdinalIgnoreCase) >= 0);
		}
	}
}
