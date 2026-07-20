using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

using DotNetCheck.DotNet;
using DotNetCheck.Models;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Surfaces multi-root .NET installations where the effective root — the one uno-check and
	/// IDE/devserver tooling resolve (DOTNET_ROOT-family variables first) — differs from the
	/// root behind the <c>dotnet</c> found on PATH. Workload and SDK maintenance commands typed
	/// in a terminal hit the PATH root, so on such machines manual repairs silently land on the
	/// wrong installation (see https://github.com/unoplatform/uno.check/issues/542).
	/// </summary>
	public class DotNetRootsCheckup : Checkup
	{
		public override string Id => "dotnetroots";

		public override string Title => ".NET installation roots";

		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new[] { new CheckupDependency("dotnet") };

		public override Task<DiagnosticResult> Examine(SharedState state)
		{
			var (environmentRootVariable, environmentRootValue) = ResolveDotNetRootEnvironment();
			var environmentRoot = NormalizeRoot(environmentRootValue);

			// The host gives the architecture-specific variables precedence over DOTNET_ROOT,
			// while DotNetSdk only considers the latter — so when an arch-specific variable
			// designates an existing root, it is the one tooling actually resolves.
			var effectiveRoot = environmentRoot != null && Directory.Exists(environmentRoot)
				? environmentRoot
				: NormalizeRoot(new DotNetSdk(state).DotNetSdkLocation?.FullName);

			var pathRoot = NormalizeRoot(ResolvePathDotnetRoot());

			foreach (var root in EnumerateKnownRoots())
			{
				ReportStatus($"Found .NET root: {root}", null);
			}

			if (effectiveRoot != null)
			{
				ReportStatus($"Effective root ({environmentRootVariable ?? "DOTNET_ROOT"}-first resolution, used by uno-check and IDE/devserver tooling): {effectiveRoot}", null);
			}

			if (pathRoot != null)
			{
				ReportStatus($"PATH root (used by terminal 'dotnet' commands): {pathRoot}", null);
			}

			if (effectiveRoot == null || pathRoot == null || RootsEqual(effectiveRoot, pathRoot))
			{
				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			var effectiveDotnetCommandPath = Path.Join(effectiveRoot, DotNetSdk.DotNetExeName);
			var message =
				$"Two different .NET installations are in play: `dotnet` on PATH resolves to '{pathRoot}' " +
				$"while the effective root is '{effectiveRoot}'" +
				(environmentRoot != null ? $" ({environmentRootVariable}={environmentRoot})" : string.Empty) + ". " +
				"IDE and devserver tooling (including Uno Hot Reload) use the effective root; terminal commands use the PATH one. " +
				$"SDK and workload maintenance (e.g. `dotnet workload update`) must target the effective root explicitly: " +
				$"'{effectiveDotnetCommandPath} workload update'. " +
				"Alternatively align DOTNET_ROOT and PATH on a single installation.";

			return Task.FromResult(new DiagnosticResult(Status.Warning, this, message));
		}

		/// <summary>
		/// Resolves the DOTNET_ROOT-family variable the .NET host would use, following its
		/// probing order: <c>DOTNET_ROOT_&lt;ARCH&gt;</c> (net6+ hosts), then
		/// <c>DOTNET_ROOT(x86)</c> for 32-bit processes, then <c>DOTNET_ROOT</c>.
		/// </summary>
		internal static (string Variable, string Value) ResolveDotNetRootEnvironment()
		{
			var archVariable = $"DOTNET_ROOT_{RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}";
			var variables = new[] { archVariable, Environment.Is64BitProcess ? null : "DOTNET_ROOT(x86)", "DOTNET_ROOT" };

			foreach (var variable in variables.Where(v => v != null))
			{
				var value = Environment.GetEnvironmentVariable(variable);
				if (!string.IsNullOrEmpty(value))
					return (variable, value);
			}

			return (null, null);
		}

		private static string ResolvePathDotnetRoot()
			=> ResolvePathDotnetRoot(Environment.GetEnvironmentVariable("PATH") ?? string.Empty, DotNetSdk.DotNetExeName);

		internal static string ResolvePathDotnetRoot(string pathValue, string exeName)
		{
			// A bare file name is guaranteed here, so the join below cannot be
			// short-circuited by a rooted second segment.
			exeName = Path.GetFileName(exeName);
			if (string.IsNullOrEmpty(exeName))
				return null;

			foreach (var entry in pathValue.Split(Path.PathSeparator)
				.Select(entry => entry.Trim().Trim('"')) // Quoted entries occur in Windows PATH values.
				.Where(entry => !string.IsNullOrWhiteSpace(entry)))
			{
				string candidate;
				try
				{
					candidate = Path.Join(entry, exeName);
					if (!File.Exists(candidate))
						continue;
				}
				catch (Exception)
				{
					// Malformed PATH entry (invalid chars, too long, ...); skip it rather
					// than letting it crash this informational checkup.
					continue;
				}

				return Path.GetDirectoryName(ResolveLinks(candidate));
			}

			return null;
		}

		internal static string ResolveLinks(string file)
		{
#if NET6_0_OR_GREATER
			try
			{
				var resolved = File.ResolveLinkTarget(file, returnFinalTarget: true);
				if (resolved != null)
					return resolved.FullName;
			}
			catch (Exception)
			{
				// Broken link, restricted path or unsupported filesystem; fall back to the
				// literal path rather than failing this informational checkup.
			}

			return file;
#else
			if (Util.IsWindows)
				return file;

			// realpath follows the whole chain (e.g. /usr/bin/dotnet → /usr/lib/dotnet/dotnet)
			// and exists on both macOS and Linux; BSD readlink on older macOS lacks -f, so
			// readlink is only the fallback. Both are invoked directly (no system shell).
			return TryResolveLinkWithTool("realpath", $"\"{file}\"")
				?? TryResolveLinkWithTool("readlink", $"-f \"{file}\"")
				?? file;
#endif
		}

#if !NET6_0_OR_GREATER
		private static string TryResolveLinkWithTool(string tool, string args)
		{
			try
			{
				var result = new ShellProcessRunner(new ShellProcessRunnerOptions(tool, args) { UseSystemShell = false }).WaitForExit();
				var output = result.GetOutput()?.Trim();
				return result.Success && !string.IsNullOrEmpty(output) ? output : null;
			}
			catch (Exception)
			{
				// Tool missing or not runnable on this platform; the caller tries the next one.
				return null;
			}
		}
#endif

		private static IEnumerable<string> EnumerateKnownRoots()
		{
			var candidates = new List<string>();

			var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			if (!string.IsNullOrEmpty(home))
				candidates.Add(Path.Join(home, ".dotnet"));

			if (Util.IsWindows)
			{
				candidates.Add(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"));
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

			// Only roots that actually carry SDKs matter for the divergence analysis.
			foreach (var candidate in candidates.Where(candidate => Directory.Exists(Path.Join(candidate, "sdk"))))
			{
				yield return NormalizeRoot(candidate);
			}
		}

		internal static string NormalizeRoot(string path)
		{
			if (string.IsNullOrEmpty(path))
				return null;

			try
			{
				return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
			}
			catch (Exception)
			{
				// A malformed DOTNET_ROOT* (or other) value must not crash the checkup;
				// treat it as no root at all.
				return null;
			}
		}

		private static bool RootsEqual(string a, string b)
			=> string.Equals(a, b, Util.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
	}
}
