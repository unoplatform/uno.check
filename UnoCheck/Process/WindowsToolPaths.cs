using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace DotNetCheck
{
	/// <summary>
	/// Finds Windows tools where they are installed. A tool started by bare name makes Windows search every PATH
	/// directory, and in a sandboxed host each of those reads asks the user (issue #646). The bare name stays the
	/// last resort.
	/// </summary>
	internal static class WindowsToolPaths
	{
		internal const string GitName = "git";
		internal const string PowerShellName = "powershell";

		const string GitRegistryKey = @"SOFTWARE\GitForWindows";
		const string GitRegistryInstallPath = "InstallPath";

		/// <summary>The command that starts Git: its full path when a known install holds it, otherwise its bare name.</summary>
		/// <returns>A full path, or <c>git</c>.</returns>
		internal static string Git()
			=> Util.IsWindows ? ResolveGit(GitInstallRoots(), File.Exists) : GitName;

		/// <summary>The command that starts Windows PowerShell: its full path in the system directory, otherwise its bare name.</summary>
		/// <returns>A full path, or <c>powershell</c>.</returns>
		internal static string PowerShell()
			=> Util.IsWindows ? ResolvePowerShell(Environment.SystemDirectory, File.Exists) : PowerShellName;

		internal static string ResolvePowerShell(string systemDirectory, Func<string, bool> fileExists)
		{
			var candidate = Path.Join(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
			return fileExists(candidate) ? candidate : PowerShellName;
		}

		internal static string ResolveGit(IEnumerable<string> installRoots, Func<string, bool> fileExists)
		{
			foreach (var root in installRoots)
			{
				if (string.IsNullOrWhiteSpace(root))
					continue;

				var candidate = Path.Join(root, "cmd", "git.exe");
				if (fileExists(candidate))
					return candidate;
			}

			return GitName;
		}

		// Git for Windows records its folder in the registry; the folders after it are its default install locations.
		static IEnumerable<string> GitInstallRoots()
		{
#pragma warning disable CA1416 // Only reached on Windows: Git() checks Util.IsWindows
			yield return ReadGitInstallPath(Registry.LocalMachine);
			yield return ReadGitInstallPath(Registry.CurrentUser);
#pragma warning restore CA1416
			yield return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git");
			yield return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git");
			yield return Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git");
		}

		static string ReadGitInstallPath(RegistryKey hive)
		{
			try
			{
#pragma warning disable CA1416 // Only reached on Windows: Git() checks Util.IsWindows
				using var key = hive.OpenSubKey(GitRegistryKey, writable: false);
				return key?.GetValue(GitRegistryInstallPath)?.ToString();
#pragma warning restore CA1416
			}
			catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
			{
				Util.Exception(ex);
				return null;
			}
		}
	}
}
