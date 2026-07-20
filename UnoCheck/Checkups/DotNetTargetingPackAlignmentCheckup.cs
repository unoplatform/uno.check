using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using DotNetCheck.DotNet;
using DotNetCheck.Models;
using DotNetCheck.Solutions;
using Newtonsoft.Json.Linq;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Verifies that the <c>Microsoft.NETCore.App</c> version pinned for browser-wasm by the
	/// installed mono-toolchain workload manifests has its targeting pack available — either
	/// under the effective root's <c>packs/</c> folder or in the NuGet cache (materialized by a
	/// restore-time <c>PackageDownload</c>). When it is missing, regular builds still work
	/// (restore downloads the pack) but restore-less design-time builds — Uno Hot Reload —
	/// silently produce compilations without any framework reference (the .NET SDK deliberately
	/// emits no error under <c>DesignTimeBuild=true</c>). See
	/// https://github.com/unoplatform/uno.check/issues/542 and
	/// https://github.com/unoplatform/uno/issues/23780.
	/// </summary>
	public class DotNetTargetingPackAlignmentCheckup : Checkup
	{
		private const string MonoToolchainManifestId = "microsoft.net.workload.mono.toolchain.current";
		private const string BrowserWasmRuntimePackPrefix = "Microsoft.NETCore.App.Runtime.Mono.browser-wasm";
		private const string TargetingPackName = "Microsoft.NETCore.App.Ref";

		public override string Id => "dotnettargetingpacks";

		public override string Title => ".NET wasm targeting-pack alignment";

		public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
			=> new[] { new CheckupDependency("dotnet") };

		public override TargetPlatform GetApplicableTargets(Manifest.Manifest manifest)
			=> TargetPlatform.WebAssembly;

		public override Task<DiagnosticResult> Examine(SharedState state)
		{
			var root = ResolveEffectiveRoot(state);

			if (root == null || !root.Exists)
			{
				// The dotnet checkup (declared dependency) reports the missing SDK itself.
				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			var misaligned = new List<(string ManifestPath, string PinnedVersion)>();

			foreach (var manifestFile in EnumerateMonoToolchainManifests(root.FullName))
			{
				string pinnedVersion;
				try
				{
					pinnedVersion = GetPinnedBrowserWasmRuntimeVersion(File.ReadAllText(manifestFile));
				}
				catch (Exception ex)
				{
					ReportStatus($"Could not read workload manifest '{manifestFile}': {ex.Message}", null);
					continue;
				}

				if (string.IsNullOrEmpty(pinnedVersion))
					continue;

				if (IsTargetingPackAvailable(root.FullName, pinnedVersion))
				{
					ReportStatus($"Manifest '{manifestFile}' pins {TargetingPackName} {pinnedVersion}: available.", null);
					continue;
				}

				misaligned.Add((manifestFile, pinnedVersion));
			}

			if (misaligned.Count == 0)
			{
				return Task.FromResult(DiagnosticResult.Ok(this));
			}

			var installedPacks = DescribeInstalledTargetingPacks(root.FullName);
			var details = string.Join(" ", misaligned.ConvertAll(m =>
				$"The workload manifest '{m.ManifestPath}' pins the browser-wasm runtime to {TargetingPackName} {m.PinnedVersion}, which is neither installed (installed: {installedPacks}) nor in the NuGet cache."));

			var message =
				$"{details} Regular builds survive by downloading the pack at restore, but restore-less design-time builds " +
				"(Uno Hot Reload) silently produce compilations without any .NET framework reference, blocking hot reload " +
				"for WebAssembly. Running 'dotnet workload update' on this .NET root realigns the manifests with the SDK.";

			// The fix must drive the muxer of the root that was examined, not whatever
			// another resolution would pick.
			var dotnetExePath = Path.Join(root.FullName, DotNetSdk.DotNetExeName);
			var suggestion = new Suggestion(
				"Update the workload manifests to match the installed SDK",
				$"Runs '{dotnetExePath} workload update' (root: {root.FullName}).",
				new DotNetWorkloadUpdateSolution(dotnetExePath));

			return Task.FromResult(new DiagnosticResult(Status.Error, this, message, suggestion));
		}

		/// <summary>
		/// Resolves the effective root with the same DOTNET_ROOT-family host probing order as
		/// <see cref="DotNetRootsCheckup"/>: an arch-specific variable outranks DOTNET_ROOT —
		/// which is all <see cref="DotNetSdk"/> considers — so alignment is examined (and the
		/// fix muxer chosen) on the installation tooling actually resolves.
		/// </summary>
		private static DirectoryInfo ResolveEffectiveRoot(SharedState state)
		{
			var (_, environmentRoot) = DotNetRootsCheckup.ResolveDotNetRootEnvironment();

			return environmentRoot != null && Directory.Exists(environmentRoot)
				? new DirectoryInfo(environmentRoot)
				: new DotNetSdk(state).DotNetSdkLocation;
		}

		/// <summary>
		/// Enumerates the mono-toolchain <c>WorkloadManifest.json</c> files across the SDK bands
		/// of <paramref name="dotnetRoot"/>, supporting both layouts: the flat one
		/// (<c>sdk-manifests/&lt;band&gt;/&lt;id&gt;/WorkloadManifest.json</c>) and the versioned one
		/// (<c>sdk-manifests/&lt;band&gt;/&lt;id&gt;/&lt;manifest-version&gt;/WorkloadManifest.json</c>).
		/// </summary>
		internal static IEnumerable<string> EnumerateMonoToolchainManifests(string dotnetRoot)
		{
			var manifestsRoot = Path.Join(dotnetRoot, "sdk-manifests");
			if (!Directory.Exists(manifestsRoot))
				yield break;

			foreach (var manifestDir in SafeEnumerateDirectories(manifestsRoot)
				.Select(band => Path.Join(band, MonoToolchainManifestId))
				.Where(Directory.Exists))
			{
				var flat = Path.Join(manifestDir, "WorkloadManifest.json");
				if (File.Exists(flat))
					yield return flat;

				foreach (var versioned in SafeEnumerateDirectories(manifestDir)
					.Select(versionDir => Path.Join(versionDir, "WorkloadManifest.json"))
					.Where(File.Exists))
				{
					yield return versioned;
				}
			}
		}

		/// <summary>
		/// Extracts the <c>Microsoft.NETCore.App</c> version the manifest pins for browser-wasm,
		/// from the version of its <c>Microsoft.NETCore.App.Runtime.Mono.browser-wasm</c> pack.
		/// Returns <see langword="null"/> when the manifest carries no such pack.
		/// </summary>
		internal static string GetPinnedBrowserWasmRuntimeVersion(string manifestJson)
		{
			var manifest = JObject.Parse(manifestJson);

			if (manifest["packs"] is not JObject packs)
				return null;

			return packs.Properties()
				.Where(pack => pack.Name.StartsWith(BrowserWasmRuntimePackPrefix, StringComparison.OrdinalIgnoreCase))
				.Select(pack => pack.Value?["version"]?.ToString())
				.FirstOrDefault();
		}

		internal static bool IsTargetingPackAvailable(string dotnetRoot, string version)
		{
			// The version comes from a parsed manifest: it must stay a single child segment
			// under the roots below, so reject anything empty, rooted, or carrying
			// separators/traversal sequences instead of probing outside those roots.
			if (string.IsNullOrWhiteSpace(version)
				|| Path.IsPathRooted(version)
				|| version.Contains("..")
				|| version.IndexOfAny(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }) >= 0)
			{
				return false;
			}

			if (Directory.Exists(Path.Join(dotnetRoot, "packs", TargetingPackName, version)))
				return true;

			// A prior restore may have materialized the PackageDownload into the NuGet cache,
			// which design-time builds resolve from as well.
			var nugetRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
			if (string.IsNullOrEmpty(nugetRoot))
			{
				var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
				nugetRoot = string.IsNullOrEmpty(home) ? null : Path.Join(home, ".nuget", "packages");
			}

			return nugetRoot != null
				&& Directory.Exists(Path.Join(nugetRoot, TargetingPackName.ToLowerInvariant(), version));
		}

		private static string DescribeInstalledTargetingPacks(string dotnetRoot)
		{
			try
			{
				var packsDir = Path.Join(dotnetRoot, "packs", TargetingPackName);
				if (!Directory.Exists(packsDir))
					return "none";

				var versions = Directory.EnumerateDirectories(packsDir).Select(Path.GetFileName).ToList();

				return versions.Count == 0 ? "none" : string.Join(", ", versions);
			}
			catch (Exception)
			{
				// This only feeds the diagnostic message; an unreadable packs folder must not
				// crash the checkup while it is reporting a failure.
				return "unknown";
			}
		}

		/// <summary>
		/// Enumerates sub-directories, treating unreadable directories (ACLs, transient IO)
		/// as empty so a filesystem hiccup does not crash the whole checkup.
		/// </summary>
		private static string[] SafeEnumerateDirectories(string path)
		{
			try
			{
				return Directory.GetDirectories(path);
			}
			catch (Exception)
			{
				return Array.Empty<string>();
			}
		}
	}
}
