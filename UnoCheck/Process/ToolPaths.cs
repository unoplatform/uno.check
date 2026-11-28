using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace DotNetCheck
{
	/// <summary>
	/// Finds tools where they are installed. On Windows a tool started by bare name makes the system search every
	/// PATH directory, and a sandboxed host asks the user for each of those reads. The bare name stays the last resort.
	/// </summary>
	internal static class ToolPaths
	{
		internal const string GitName = "git";
		internal const string PowerShellName = "powershell";

		const string GitRegistryKey = @"SOFTWARE\GitForWindows";
		const string GitRegistryInstallPath = "InstallPath";

		/// <summary>The command that starts Git: its full path when a known install is the one on the PATH, otherwise its bare name.</summary>
		/// <returns>A full path, or <c>git</c>.</returns>
		internal static string Git()
		{
			if (!Util.IsWindows)
				return GitName;

#pragma warning disable CA1416 // Windows only: guarded by Util.IsWindows above, which the analyzer can't see
			var roots = GitInstallRoots(
				ReadGitInstallPaths(RegistryHive.LocalMachine),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
				Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
				ReadGitInstallPaths(RegistryHive.CurrentUser),
				Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
				Util.IsAdmin());
#pragma warning restore CA1416

			return ResolveGit(roots, File.Exists, Environment.GetEnvironmentVariable("PATH"));
		}

		/// <summary>The command that starts Windows PowerShell: its full path in the system directory, otherwise its bare name.</summary>
		/// <returns>A full path, or <c>powershell</c>.</returns>
		internal static string PowerShell()
			=> Util.IsWindows ? ResolvePowerShell(Environment.SystemDirectory, File.Exists) : PowerShellName;

		/// <summary>
		/// Starts a Windows PowerShell command on its own modules only, so a module-qualified cmdlet loads even when the
		/// caller's PSModulePath names another PowerShell's modules, as PowerShell 7 and GitHub's Windows runner do.
		/// </summary>
		internal const string OwnModulesOnly = "$env:PSModulePath = $PSHOME + '\\Modules'; ";

		/// <summary>PowerShell arguments that run <paramref name="command"/> without reading the user's profile script or waiting for input.</summary>
		/// <param name="command">The PowerShell command to run.</param>
		/// <returns>The argument string.</returns>
		internal static string PowerShellCommand(string command)
			=> "-NoProfile -NonInteractive -Command " + command;

		internal static string ResolvePowerShell(string systemDirectory, Func<string, bool> fileExists)
		{
			if (!IsLocalFullPath(systemDirectory))
				return PowerShellName;

			var candidate = Path.Join(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
			return fileExists(candidate) ? candidate : PowerShellName;
		}

		// Machine-wide locations come first. The user-writable ones are left out when elevated, so a value any
		// process of the user can set never chooses what runs as administrator.
		internal static IEnumerable<string> GitInstallRoots(
			IEnumerable<string> machineRegistryPaths,
			string programFiles,
			string programFilesX86,
			IEnumerable<string> userRegistryPaths,
			string localApplicationData,
			bool elevated)
		{
			foreach (var path in machineRegistryPaths)
				yield return path;

			yield return JoinIfSet(programFiles, "Git");
			yield return JoinIfSet(programFilesX86, "Git");

			if (elevated)
				yield break;

			foreach (var path in userRegistryPaths)
				yield return path;

			yield return JoinIfSet(localApplicationData, "Programs", "Git");
		}

		// The Git check means "git works from a terminal", so an install is only used when its cmd folder is on the
		// PATH. That is a comparison of text: no folder is read.
		internal static string ResolveGit(IEnumerable<string> installRoots, Func<string, bool> fileExists, string pathValue)
			=> installRoots
				.Where(IsLocalFullPath)
				.Select(root => Path.Join(root, "cmd"))
				.Where(cmdFolder => IsOnPath(cmdFolder, pathValue))
				.Select(cmdFolder => Path.Join(cmdFolder, "git.exe"))
				.FirstOrDefault(fileExists)
				?? GitName;

		internal static bool IsLocalFullPath(string path)
			=> !string.IsNullOrWhiteSpace(path)
				&& Path.IsPathFullyQualified(path)
				&& !path.StartsWith(@"\\", StringComparison.Ordinal)
				&& !path.StartsWith("//", StringComparison.Ordinal);

		internal static bool IsOnPath(string folder, string pathValue)
		{
			var wanted = TrimSeparators(folder);

			return (pathValue ?? string.Empty)
				.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
				.Select(entry => TrimSeparators(Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'))))
				.Any(entry => string.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase));
		}

		static string TrimSeparators(string path)
			=> path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		static string JoinIfSet(string root, params string[] segments)
			=> string.IsNullOrWhiteSpace(root) ? null : segments.Aggregate(root, (path, segment) => Path.Join(path, segment));

#pragma warning disable CA1416 // Windows only: reached from Git() alone, after its Util.IsWindows guard
		// Git for Windows records its folder under the 64-bit or the 32-bit view, depending on the installer.
		static IEnumerable<string> ReadGitInstallPaths(RegistryHive hive)
		{
			yield return ReadGitInstallPath(hive, RegistryView.Registry64);
			yield return ReadGitInstallPath(hive, RegistryView.Registry32);
		}

		static string ReadGitInstallPath(RegistryHive hive, RegistryView view)
		{
			try
			{
				using var baseKey = RegistryKey.OpenBaseKey(hive, view);
				using var key = baseKey.OpenSubKey(GitRegistryKey, writable: false);
				return key?.GetValue(GitRegistryInstallPath) as string;
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
			{
				Util.Exception(ex);
				return null;
			}
		}
#pragma warning restore CA1416
	}
}
