using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Spectre.Console;

namespace DotNetCheck
{
	public class Util
	{
		public static string[] BaseSkips = ["git", "linuxninja", "psexecpolicy", "windowspyhtonInstallation"];
        public static string[] RiderSkips = ["vswin","vswinworkloads"];
        public static string[] VSCodeSkips = ["vswin","vswinworkloads"];
        public static string[] VSSkips = ["vswin","vswinworkloads"];

        public static void UpdateSkips(CheckSettings settings, string[] skips)
        {
	        var currentSkips = settings.Skip?.ToList() ?? [];
	        currentSkips.AddRange(skips);
	        settings.Skip = currentSkips.Distinct().ToArray();
        }
		public static void LogAlways(string message)
		{
			Console.WriteLine(message);
		}

		public static void Log(string message)
		{
			if (Verbose)
			{
				Console.WriteLine(message);

				if (!string.IsNullOrEmpty(LogFile))
				{
					File.AppendAllText(LogFile, $"{message}{Environment.NewLine}");
				}
			}
		}

		public static void Exception(Exception ex)
		{
			if (Verbose)
			{
				AnsiConsole.WriteException(ex);

				if (!string.IsNullOrEmpty(LogFile))
				{
					File.AppendAllText(LogFile, $"{ex}{Environment.NewLine}");
				}
			}
		}

		public static bool Verbose { get; set; }
		public static string LogFile { get; set; }
		public static bool CI { get; set; }
		public static bool NonInteractive { get; set; }

		public static Dictionary<string, string> EnvironmentVariables { get; } = new Dictionary<string, string>();

		public static Platform Platform
		{
			get
			{
				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
					return Platform.Windows;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
					return Platform.OSX;

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD))
					return Platform.Linux;

				return Platform.Windows;
			}
		}

		public static bool Is64
		{
			get
			{
				if (Platform == Platform.Windows && RuntimeInformation.OSArchitecture == Architecture.X86)
					return false;
				return true;
			}
		}

		public static bool IsArm64
			=> RuntimeInformation.OSArchitecture == Architecture.Arm64;

		public static bool IsWindows
			=> Platform == Platform.Windows;

		public static bool IsMac
			=> Platform == Platform.OSX;

		public static bool IsLinux
			=> Platform == Platform.Linux;

		public const string ArchWin = "win";
		public const string ArchWin64 = "win64";
		public const string ArchWinArm64 = "winArm64";
		public const string ArchOsx = "osx";
		public const string ArchOsxArm64 = "osxArm64";

		public static bool IsArchCompatible(string arch)
        {
			if (string.IsNullOrEmpty(arch))
				return true;

			arch = arch.ToLowerInvariant().Trim();

			if (arch == ArchWin)
				return IsWindows && !Is64 && !IsArm64;
			else if(arch == ArchWin64)
				return IsWindows && Is64 && !IsArm64;
			else if (arch == ArchWinArm64)
				return IsWindows && IsArm64;
			else if (arch == ArchOsx)
				return IsMac && !IsArm64;
			else if (arch == ArchOsxArm64)
				return IsMac && IsArm64;

			return false;
		}

		[DllImport("libc")]
#pragma warning disable IDE1006 // Naming Styles
		static extern uint getuid();
#pragma warning restore IDE1006 // Naming Styles


		public static bool IsAdmin()
		{
			try
			{
				if (IsWindows)
				{
#pragma warning disable IDE0079 // remove unnecessary pragma suppression
#pragma warning disable CA1416 // Validate platform compatibility as this is only executed on windows
					using (var identity = System.Security.Principal.WindowsIdentity.GetCurrent())
					{
						var principal = new System.Security.Principal.WindowsPrincipal(identity);
						if (!principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator))
						{
							return false;
						}
#pragma warning restore CA1416 // Validate platform compatibility
#pragma warning restore IDE0079 // remove unnecessary pragma suppression
					}
				}
				else if (getuid() != 0)
				{
					return false;
				}
			}
			catch
			{
				return false;
			}

			return true;
		}

		public static bool Delete(string path, bool isFile)
		{
			if (!Util.IsWindows)
			{
				// Delete the destination as su
				//		sudo rm -rf destination
				var args = $"-c 'sudo rm -rf \"{path}\"'";

				if (Verbose)
					Console.WriteLine($"{ShellProcessRunner.MacOSShell} {args}");

				var p = new ShellProcessRunner(new ShellProcessRunnerOptions(ShellProcessRunner.MacOSShell, args)
				{
					RedirectOutput = Verbose
				});

				p.WaitForExit();

				return true;
			}
			else
			{
				try
				{
					if (isFile)
						File.Delete(path);
					else
						Directory.Delete(path, true);

					return true;
				}
				catch (Exception ex)
				{
					Util.Exception(ex);
				}
			}

			return false;
		}

		public static Task<ShellProcessRunner.ShellProcessResult> ShellCommand(string cmd, string workingDir, bool verbose, string[] args)
			=> ShellCommand(cmd, workingDir, verbose, System.Threading.CancellationToken.None, args);

		public static Task<ShellProcessRunner.ShellProcessResult> ShellCommand(string cmd, string workingDir, bool verbose, System.Threading.CancellationToken cancellationToken, string[] args)
		{
			var cli = new ShellProcessRunner(new ShellProcessRunnerOptions(cmd, string.Join(" ", args), cancellationToken) { WorkingDirectory = workingDir, Verbose = verbose } );
			return Task.FromResult(cli.WaitForExit());
		}

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudo(string cmd, string[] args)
			=> WrapShellCommandWithSudo(cmd, null, false, System.Threading.CancellationToken.None, args);

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudo(string cmd, string workingDir, bool verbose, string[] args)
			=> WrapShellCommandWithSudo(cmd, workingDir, verbose, System.Threading.CancellationToken.None, args);

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudoNoPrompt(string cmd, string workingDir, bool verbose, string[] args)
			=> WrapShellCommandWithSudo(cmd, workingDir, verbose, System.Threading.CancellationToken.None, true, args);

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudoNoPrompt(string cmd, string workingDir, bool verbose, System.Threading.CancellationToken cancellationToken, string[] args)
			=> WrapShellCommandWithSudo(cmd, workingDir, verbose, cancellationToken, true, args);

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudo(string cmd, string workingDir, bool verbose, System.Threading.CancellationToken cancellationToken, string[] args)
			=> WrapShellCommandWithSudo(cmd, workingDir, verbose, cancellationToken, false, args);

		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudo(string cmd, string workingDir, bool verbose, System.Threading.CancellationToken cancellationToken, bool noPrompt, string[] args)
		{
			var actualCmd = cmd;
			var actualArgs = string.Join(" ", args);

			if (!Util.IsWindows)
			{
				actualCmd = ShellProcessRunner.MacOSShell;
				var sudoPrefix = noPrompt ? "sudo -n" : "sudo";
				// Escape single quotes in command and args to prevent shell injection
				var escapedCmd = cmd.Replace("'", "'\\''")
					;
				var escapedArgs = actualArgs.Replace("'", "'\\''")
					;
				actualArgs = $"-c '{sudoPrefix} {escapedCmd} {escapedArgs}'";
			}

			var cli = new ShellProcessRunner(new ShellProcessRunnerOptions(actualCmd, actualArgs, cancellationToken) { WorkingDirectory = workingDir, Verbose = verbose } );
			return Task.FromResult(cli.WaitForExit());
		}

		/// <summary>
		/// Runs the command with sudo by prompting for the password in-process first,
		/// then piping it to <c>sudo -S</c> via stdin.
		/// Output is not captured (goes directly to the terminal) — use exit code for success/failure.
		/// This does not rely on sudo prompting on <c>/dev/tty</c>; it avoids the macOS sudo PTY relay
		/// that can hang indefinitely when started from <c>.NET Process.Start</c>.
		/// </summary>
		public static Task<ShellProcessRunner.ShellProcessResult> WrapShellCommandWithSudoInteractive(string cmd, string workingDir, bool verbose, System.Threading.CancellationToken cancellationToken, string[] args)
		{
			var actualCmd = cmd;
			var actualArgs = string.Join(" ", args);

			if (!Util.IsWindows)
			{
				// Prompt the user for the sudo password before starting the process.
				var password = ReadPasswordFromConsole();
				if (password == null)
				{
					return Task.FromResult(new ShellProcessRunner.ShellProcessResult(
						new List<string>(),
						new List<string>
						{
							"Unable to perform sudo elevation because no password could be read from the console. " +
							"This can happen when standard input is redirected or the terminal is non-interactive. " +
							"Please rerun this command in an interactive terminal or configure passwordless sudo."
						},
						-1));
				}

				// sudo -S: read password from stdin (pipe) instead of /dev/tty.
				// This avoids the macOS PTY relay (exec_pty) that hangs when the child
				// process exits but the relay's stdin (the terminal) stays open.
				// sudo -p "": suppress sudo's own "Password:" prompt since we already prompted.
				actualCmd = "sudo";
				actualArgs = $"-S -p \"\" {cmd} {actualArgs}";

				var cli = new ShellProcessRunner(new ShellProcessRunnerOptions(actualCmd, actualArgs, cancellationToken)
				{
					WorkingDirectory = workingDir,
					Verbose = verbose,
					RedirectInput = true,
					RedirectOutput = false,
					UseSystemShell = false
				});

				// Pipe the password to sudo's stdin, then close the stream so sudo
				// (and any child processes) see EOF. This ensures the PTY relay's
				// stdin side is closed, allowing it to exit after the child finishes.
				try
				{
					cli.Write(password + "\n");
					cli.FlushAndCloseInput();
				}
				catch (System.IO.IOException)
				{
					// Process exited before we could write (e.g., sudo not found).
				}
				catch (ObjectDisposedException)
				{
					// Process was already disposed.
				}
				catch (InvalidOperationException)
				{
					// StandardInput not available.
				}

				return Task.FromResult(cli.WaitForExit());
			}

			var fallback = new ShellProcessRunner(new ShellProcessRunnerOptions(actualCmd, actualArgs, cancellationToken) { WorkingDirectory = workingDir, Verbose = verbose });
			return Task.FromResult(fallback.WaitForExit());
		}

		/// <summary>
		/// Reads a password from the console with masked input (characters are not echoed).
		/// Returns null if the user enters an empty password.
		/// </summary>
		internal static string ReadPasswordFromConsole()
		{
			try
			{
				if (Console.IsInputRedirected)
					return null;

				Console.Write("Password: ");
				var password = new System.Text.StringBuilder();
				while (true)
				{
					var key = Console.ReadKey(intercept: true);
					if (key.Key == ConsoleKey.Enter)
						break;
					if (key.Key == ConsoleKey.Backspace)
					{
						if (password.Length > 0)
							password.Remove(password.Length - 1, 1);
					}
					else if (!char.IsControl(key.KeyChar))
					{
						password.Append(key.KeyChar);
					}
				}
				Console.WriteLine();
				return password.Length > 0 ? password.ToString() : null;
			}
			catch (InvalidOperationException)
			{
				// Console.ReadKey is unavailable (e.g., stdin redirected in a wrapper script).
				return null;
			}
		}

		public static async Task<bool> WrapCopyWithShellSudo(string destination, bool isFile, Func<string, Task<bool>> wrapping)
		{
			var intermediate = destination;

			var destDir = intermediate;
			if (isFile)
				destDir = new FileInfo(destDir).Directory.FullName;

			if (!Util.IsWindows)
			{
				if (isFile)
					intermediate = Path.Combine(Path.GetTempFileName());
				else
					intermediate = Path.Combine(Path.GetTempPath(), System.Guid.NewGuid().ToString());
			}

			// If windows, we'll delete the directory or file at destination here
			if (Util.IsWindows)
			{
				var dir = isFile ? new FileInfo(destination).Directory.FullName : new DirectoryInfo(destination).FullName;

				if (!Directory.Exists(dir))
					Directory.CreateDirectory(dir);
			}

			var r = await wrapping(intermediate);

			if (r && !Util.IsWindows)
			{
				// Copy a file to a destination as su
				//		sudo mkdir -p destDir && sudo cp -pP intermediate destination

				// Copy a folder recursively to the destination as su
				//		sudo mkdir -p destDir && sudo cp -pPR intermediate/ destination
				var args = isFile
					? $"-c 'sudo mkdir -p \"{destDir}\" && sudo cp -pP \"{intermediate}\" \"{destination}\"'"
					: $"-c 'sudo mkdir -p \"{destDir}\" && sudo cp -pPR \"{intermediate}/\" \"{destination}\"'"; // note the / at the end of the dir

				if (Verbose)
					Console.WriteLine($"{ShellProcessRunner.MacOSShell} {args}");

				var p = new ShellProcessRunner(new ShellProcessRunnerOptions(ShellProcessRunner.MacOSShell, args)
				{
					RedirectOutput = Verbose
				});

				p.WaitForExit();
			}

			return r;
		}

		public static void DirectoryCopy(string sourceDirName, string destDirName, bool copySubDirs)
		{
			// Get the subdirectories for the specified directory.
			var dir = new DirectoryInfo(sourceDirName);

			if (!dir.Exists)
				return;

			var dirs = dir.GetDirectories();

			// If the destination directory doesn't exist, create it.
			Directory.CreateDirectory(destDirName);

			// Get the files in the directory and copy them to the new location.
			var files = dir.GetFiles();
			foreach (var file in files)
			{
				var tempPath = Path.Combine(destDirName, file.Name);
				file.CopyTo(tempPath, false);
			}

			// If copying subdirectories, copy them and their contents to new location.
			if (copySubDirs)
			{
				foreach (var subdir in dirs)
				{
					var tempPath = Path.Combine(destDirName, subdir.Name);
					DirectoryCopy(subdir.FullName, tempPath, copySubDirs);
				}
			}
		}
	}

	public enum Platform
	{
		Windows,
		OSX,
		Linux
	}
}
