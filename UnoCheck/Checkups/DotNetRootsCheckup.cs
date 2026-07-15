using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

using DotNetCheck.DotNet;
using DotNetCheck.Models;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Surfaces multi-root .NET installations where the effective root — the one uno-check and
	/// IDE/devserver tooling resolve (DOTNET_ROOT first) — differs from the root behind the
	/// <c>dotnet</c> found on PATH. Workload and SDK maintenance commands typed in a terminal
	/// hit the PATH root, so on such machines manual repairs silently land on the wrong
	/// installation (see https://github.com/unoplatform/uno.check/issues/542).
	/// </summary>
	public class DotNetRootsCheckup : Checkup
	{
		public override string Id => "dotnetroots";

		public override string Title => ".NET installation roots";

		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new[] { new CheckupDependency("dotnet") };

		public override Task<DiagnosticResult> Examine(SharedState state)
		{
			var effectiveRoot = NormalizeRoot(new DotNetSdk(state).DotNetSdkLocation?.FullName);
			var pathRoot = NormalizeRoot(ResolvePathDotnetRoot());
			var environmentRoot = NormalizeRoot(Environment.GetEnvironmentVariable("DOTNET_ROOT"));

			foreach (var root in EnumerateKnownRoots())
			{
				ReportStatus($"Found .NET root: {root}", null);
			}

			if (effectiveRoot != null)
			{
				ReportStatus($"Effective root (DOTNET_ROOT-first resolution, used by uno-check and IDE/devserver tooling): {effectiveRoot}", null);
			}

			if (pathRoot != null)
			{
				ReportStatus($"PATH root (used by terminal 'dotnet' commands): {pathRoot}", null);
			}

			if (effectiveRoot == null || pathRoot == null || RootsEqual(effectiveRoot, pathRoot))
			{
				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			var message =
				$"Two different .NET installations are in play: `dotnet` on PATH resolves to '{pathRoot}' " +
				$"while the effective root is '{effectiveRoot}'" +
				(environmentRoot != null ? $" (DOTNET_ROOT={environmentRoot})" : string.Empty) + ". " +
				"IDE and devserver tooling (including Uno Hot Reload) use the effective root; terminal commands use the PATH one. " +
				$"SDK and workload maintenance (e.g. `dotnet workload update`) must target the effective root explicitly: " +
				$"'{Path.Combine(effectiveRoot, DotNetSdk.DotNetExeName)} workload update'. " +
				"Alternatively align DOTNET_ROOT and PATH on a single installation.";

			return Task.FromResult(new DiagnosticResult(Status.Warning, this, message));
		}

		private static string ResolvePathDotnetRoot()
		{
			var exeName = DotNetSdk.DotNetExeName;
			var pathValue = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

			foreach (var entry in pathValue.Split(Path.PathSeparator))
			{
				if (string.IsNullOrWhiteSpace(entry))
					continue;

				string candidate;
				try
				{
					candidate = Path.Combine(entry.Trim(), exeName);
					if (!File.Exists(candidate))
						continue;
				}
				catch (ArgumentException)
				{
					// Malformed PATH entry; skip it.
					continue;
				}

				return Path.GetDirectoryName(ResolveLinks(candidate));
			}

			return null;
		}

		private static string ResolveLinks(string file)
		{
#if NET6_0_OR_GREATER
			try
			{
				var resolved = File.ResolveLinkTarget(file, returnFinalTarget: true);
				if (resolved != null)
					return resolved.FullName;
			}
			catch (IOException)
			{
				// Broken link or unsupported filesystem; fall back to the literal path.
			}

			return file;
#else
			if (Util.IsWindows)
				return file;

			// readlink -f follows the whole chain (e.g. /usr/bin/dotnet → /usr/lib/dotnet/dotnet).
			var result = new ShellProcessRunner(new ShellProcessRunnerOptions("readlink", $"-f \"{file}\"")).WaitForExit();
			var output = result.GetOutput()?.Trim();
			return result.Success && !string.IsNullOrEmpty(output) ? output : file;
#endif
		}

		private static IEnumerable<string> EnumerateKnownRoots()
		{
			var candidates = new List<string>();

			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (!string.IsNullOrEmpty(home))
				candidates.Add(Path.Combine(home, ".dotnet"));

			if (Util.IsWindows)
			{
				candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
			}
			else if (Util.IsMac)
			{
				candidates.Add("/usr/local/share/dotnet");
			}
			else
			{
				candidates.Add("/usr/lib/dotnet");
				candidates.Add("/usr/share/dotnet");
			}

			foreach (var candidate in candidates)
			{
				// Only roots that actually carry SDKs matter for the divergence analysis.
				if (Directory.Exists(Path.Combine(candidate, "sdk")))
					yield return NormalizeRoot(candidate);
			}
		}

		private static string NormalizeRoot(string path)
			=> string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

		private static bool RootsEqual(string a, string b)
			=> string.Equals(a, b, Util.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}
}
