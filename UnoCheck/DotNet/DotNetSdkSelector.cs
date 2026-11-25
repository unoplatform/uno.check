using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using DotNetCheck.Models;
using NuGet.Versioning;

namespace DotNetCheck.DotNet
{
	internal enum DotNetSdkSelectionSource
	{
		/// <summary>Asked for with <c>--dotnet-sdk-version</c>.</summary>
		Requested,

		/// <summary>Resolved by the dotnet host from the <c>global.json</c> that applies to the working directory.</summary>
		GlobalJson,

		/// <summary>Resolved by the dotnet host from a <c>global.json</c> with SDK settings but no <c>sdk.version</c>, such as <c>allowPrerelease</c>.</summary>
		GlobalJsonSettings,

		/// <summary>The newest installed SDK that fits an SDK the manifest requires.</summary>
		Manifest,

		/// <summary>The newest SDK of the manifest's .NET version when none is the one the manifest requires; the <c>dotnet</c> checkup's fix installs that one.</summary>
		ManifestFallback,

		/// <summary>No installed SDK fits the manifest, so the newest one.</summary>
		Newest,
	}

	/// <summary>
	/// What the dotnet host resolved for a <c>global.json</c> that pins an SDK: the version, or why it could not.
	/// </summary>
	internal sealed record GlobalJsonSdkResolution(string Path, NuGetVersion Version, string Failure, bool HasVersion = true);

	/// <summary>A <c>global.json</c> whose <c>sdk</c> section influences the SDK the host picks.</summary>
	internal sealed record SdkGlobalJson(string Path, bool HasVersion);

	/// <summary>
	/// The SDK that checks and installs workloads. <see cref="Sdk"/> is null when no SDK is installed
	/// or <see cref="Error"/> is set.
	/// </summary>
	internal sealed record DotNetSdkSelection(
		DotNetSdkInfo Sdk,
		DotNetSdkSelectionSource Source,
		IReadOnlyList<string> Warnings,
		string Error = null,
		string GlobalJsonPath = null);

	/// <summary>
	/// Chooses the one .NET SDK uno-check checks and installs workloads with. The workloads a manifest
	/// installs land in the feature band of the SDK running the install, so a choice that differs from the SDK
	/// a project builds with leaves that project without its workloads (issue #638).
	/// Precedence: an explicit request, then the <c>global.json</c> of the working directory as the host
	/// resolves it, then the manifest's own SDK.
	/// </summary>
	internal static class DotNetSdkSelector
	{
		/// <summary>
		/// The SDK the workloads are checked and installed with (see <see cref="Select"/>), resolving the
		/// <c>global.json</c> that applies to the current directory the way the dotnet host does.
		/// </summary>
		internal static async Task<DotNetSdkSelection> ResolveAsync(
			SharedState state,
			DotNetSdk dotnet,
			IReadOnlyCollection<DotNetSdkInfo> installed,
			IReadOnlyCollection<Manifest.DotNetSdk> required)
		{
			state.TryGetState<string>(StateKey.EntryPoint, StateKey.DotNetSdkVersion, out var requestedVersion);

			var globalJson = string.IsNullOrWhiteSpace(requestedVersion)
				? await dotnet.ResolveGlobalJsonSdk(Directory.GetCurrentDirectory())
				: null;

			return SelectInRoot(installed, dotnet.DotNetSdkLocation.FullName, required, requestedVersion, globalJson);
		}

		/// <summary>
		/// <see cref="Select"/> among the SDKs of the root whose <c>dotnet</c> runs the workload commands. An SDK the
		/// host resolved from <c>sdk.paths</c> in another installation is not usable there, as installations keep their
		/// own workload metadata.
		/// </summary>
		internal static DotNetSdkSelection SelectInRoot(
			IReadOnlyCollection<DotNetSdkInfo> installed,
			string sdkRoot,
			IReadOnlyCollection<Manifest.DotNetSdk> required,
			string requestedVersion,
			GlobalJsonSdkResolution globalJson)
		{
			var scoped = InstalledUnder(installed, sdkRoot);
			var elsewhere = new List<string>();

			if (globalJson?.Version is { } pinned && installed.Any(s => s.Version == pinned))
			{
				var other = installed.Where(s => s.Version == pinned && !scoped.Contains(s)).ToList();

				if (other.Count > 0 && !scoped.Any(s => s.Version == pinned))
				{
					// The host resolved an SDK of another installation, whose workload metadata the SDK root's
					// dotnet cannot see; checking or installing here would miss the SDK the project builds with.
					globalJson = new GlobalJsonSdkResolution(
						globalJson.Path,
						null,
						$"it resolves to .NET SDK {pinned} in {other[0].Directory.Parent?.Parent?.FullName}, outside the SDK root {sdkRoot} whose workloads uno-check manages; run uno-check with --dotnet pointing at that installation",
						globalJson.HasVersion);
				}
				else if (other.Count > 0)
				{
					elsewhere.Add($".NET SDK {pinned} is also installed in {other[0].Directory.Parent?.Parent?.FullName}. "
						+ $"If global.json selects that installation (sdk.paths), workloads are still checked and installed in the SDK root {sdkRoot}.");
				}
			}

			var selection = Select(scoped, required, requestedVersion, globalJson);

			// Only meaningful when global.json is what chose the SDK
			return elsewhere.Count == 0 || selection.Source is not (DotNetSdkSelectionSource.GlobalJson or DotNetSdkSelectionSource.GlobalJsonSettings)
				? selection
				: selection with { Warnings = [.. selection.Warnings, .. elsewhere] };
		}

		internal static DotNetSdkSelection Select(
			IReadOnlyCollection<DotNetSdkInfo> installed,
			IReadOnlyCollection<Manifest.DotNetSdk> required,
			string requestedVersion,
			GlobalJsonSdkResolution globalJson)
		{
			var warnings = new List<string>();
			required ??= [];

			if (installed is null || installed.Count == 0)
				return new DotNetSdkSelection(null, DotNetSdkSelectionSource.Newest, warnings);

			if (!string.IsNullOrWhiteSpace(requestedVersion))
			{
				var requested = FindInstalled(installed, requestedVersion);

				if (requested is null)
					return Failed($"The requested .NET SDK {requestedVersion} (--dotnet-sdk-version) is not installed. Installed: {Describe(installed)}.");

				if (required.Count > 0 && !IsSameDotNet(requested.Version, required))
				{
					return Failed($"The requested .NET SDK {requestedVersion} (--dotnet-sdk-version) cannot install the workloads of this manifest, which targets {DescribeRequired(required)}. "
						+ "Request an SDK of that .NET version, or use a manifest for the .NET version of the requested SDK (--manifest).");
				}

				return new DotNetSdkSelection(requested, DotNetSdkSelectionSource.Requested, warnings);
			}

			if (globalJson is not null)
			{
				var pinned = globalJson.Version is null ? null : installed.FirstOrDefault(s => s.Version == globalJson.Version);

				if (globalJson.Failure is not null)
				{
					warnings.Add($"global.json '{globalJson.Path}' could not be used ({globalJson.Failure}). It is ignored.");
				}
				else if (pinned is null)
				{
					warnings.Add($"global.json '{globalJson.Path}' resolves to .NET SDK {globalJson.Version}, which is not listed among the installed SDKs ({Describe(installed)}). It is ignored.");
				}
				else if (required.Count > 0 && !IsSameDotNet(pinned.Version, required))
				{
					// The manifest's rollback file is for the manifest's .NET version; installing it under another
					// SDK would not provide the workloads either, so the manifest's SDK stays in charge.
					warnings.Add($"global.json '{globalJson.Path}' selects .NET SDK {pinned.Version}, but this manifest targets {DescribeRequired(required)}. "
						+ $"Workloads are installed for the SDK the manifest requires, so builds with .NET SDK {pinned.Version} will report missing workloads (NETSDK1147). "
						+ (globalJson.HasVersion
							? "Update global.json to the .NET version of the manifest, or use a manifest that targets the .NET version of global.json (--manifest)."
							: "Its settings let the host pick the newest SDK it allows; pin the .NET version with sdk.version in global.json, or use a manifest that targets that .NET version (--manifest)."));
				}
				else
				{
					return new DotNetSdkSelection(
						pinned,
						globalJson.HasVersion ? DotNetSdkSelectionSource.GlobalJson : DotNetSdkSelectionSource.GlobalJsonSettings,
						warnings,
						GlobalJsonPath: globalJson.Path);
				}
			}

			// The newest SDK of the manifest's .NET version, as the dotnet host rolls forward from the manifest's
			// version (and builds without a global.json use it). The newest installed SDK overall could be a later
			// .NET major with another feature band.
			var compatible = installed
				.Where(s => IsCompatible(s.Version, required))
				.OrderByDescending(s => s.Version)
				.FirstOrDefault();

			if (compatible is not null)
				return new DotNetSdkSelection(compatible, DotNetSdkSelectionSource.Manifest, warnings);

			// Same .NET but older than the manifest's SDK (e.g. 10.0.204 for 10.0.303): still the .NET version
			// the manifest targets, which an explicit --dotnet-sdk-version accepts, so it is a replayable choice
			// and preferred over a later major.
			var sameDotNet = installed
				.Where(s => IsSameDotNet(s.Version, required))
				.OrderByDescending(s => s.Version)
				.FirstOrDefault();

			if (sameDotNet is not null)
			{
				warnings.Add($"The newest installed .NET SDK of the manifest's .NET version is {sameDotNet.Version}, not the {DescribeRequired(required)} this manifest requires. "
					+ $"Install {DescribeRequired(required)}.");

				return new DotNetSdkSelection(sameDotNet, DotNetSdkSelectionSource.ManifestFallback, warnings);
			}
			if (required.Count > 0)
			{
				warnings.Add($"No installed .NET SDK is of the .NET version this manifest targets ({DescribeRequired(required)}). "
					+ $"Using the newest installed one, which records the workloads for its own feature band. Install {DescribeRequired(required)}.");
			}

			return new DotNetSdkSelection(
				installed.OrderByDescending(s => s.Version).First(),
				DotNetSdkSelectionSource.Newest,
				warnings);

			DotNetSdkSelection Failed(string error)
				=> new(null, DotNetSdkSelectionSource.Requested, warnings, error);
		}

		/// <summary>
		/// Whether workloads of a manifest requiring <paramref name="required"/> can be installed with
		/// <paramref name="sdk"/> by default: the same .NET major and minor, and not older than the required
		/// version (exactly it when the manifest requires an exact one).
		/// </summary>
		internal static bool IsCompatible(NuGetVersion sdk, IEnumerable<Manifest.DotNetSdk> required)
			=> required.Any(r =>
				NuGetVersion.TryParse(r.Version, out var version)
				&& sdk.Major == version.Major
				&& sdk.Minor == version.Minor
				&& (r.RequireExact ? sdk == version : sdk >= version));

		/// <summary>
		/// Whether <paramref name="sdk"/> is of the .NET version of a required SDK. An SDK that was asked for, or that
		/// a <c>global.json</c> pinned, is what the project builds with, so an older patch or feature band than the
		/// manifest's is fine: the workloads have to be installed in that SDK's band.
		/// </summary>
		internal static bool IsSameDotNet(NuGetVersion sdk, IEnumerable<Manifest.DotNetSdk> required)
			=> required.Any(r =>
				NuGetVersion.TryParse(r.Version, out var version)
				&& sdk.Major == version.Major
				&& sdk.Minor == version.Minor);

		/// <summary>
		/// The first <c>global.json</c> from <paramref name="startDirectory"/> up, the one the dotnet host uses,
		/// when it has settings that select an SDK; otherwise null.
		/// </summary>
		internal static SdkGlobalJson FindSdkGlobalJson(string startDirectory)
		{
			for (var directory = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
			{
				var path = Path.Combine(directory.FullName, "global.json");

				if (!File.Exists(path))
					continue;

				return ReadSdkSettings(path);
			}

			return null;
		}

		/// <summary>
		/// Whether the <c>sdk</c> section of a <c>global.json</c> influences which SDK the host picks. A version is
		/// not required: <c>allowPrerelease</c> and <c>paths</c> select an SDK on their own, such as the newest stable
		/// one with <c>allowPrerelease: false</c> while a newer preview is installed. <c>rollForward</c> only counts
		/// with a version, as the host ignores it otherwise.
		/// </summary>
		private static SdkGlobalJson ReadSdkSettings(string path)
		{
			try
			{
				using var document = JsonDocument.Parse(
					File.ReadAllText(path),
					new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

				if (document.RootElement.ValueKind != JsonValueKind.Object
					|| !document.RootElement.TryGetProperty("sdk", out var sdk)
					|| sdk.ValueKind != JsonValueKind.Object)
				{
					return null;
				}

				var hasVersion = HasNonEmptyString(sdk, "version");

				return hasVersion || SdkSelectionSettings.Any(setting => sdk.TryGetProperty(setting, out var value) && value.ValueKind != JsonValueKind.Null)
					? new SdkGlobalJson(path, hasVersion)
					: null;
			}
			catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
			{
				Util.Log($"Ignoring unreadable global.json '{path}': {ex.Message}");
				return null;
			}
		}

		private static readonly string[] SdkSelectionSettings = ["allowPrerelease", "paths"];
		private static bool HasNonEmptyString(JsonElement element, string name)
			=> element.TryGetProperty(name, out var value)
				&& value.ValueKind == JsonValueKind.String
				&& !string.IsNullOrWhiteSpace(value.GetString());

		/// <summary>
		/// The SDKs that live under <paramref name="sdkRoot"/>, the installation whose <c>dotnet</c> runs the workload
		/// commands. With <c>sdk.paths</c> the host can list, and resolve to, SDKs of other installations, which keep
		/// their own workload metadata. When none is found under the root (a symlinked root, an unusual layout) the
		/// list is returned as is.
		/// </summary>
		internal static IReadOnlyCollection<DotNetSdkInfo> InstalledUnder(IReadOnlyCollection<DotNetSdkInfo> installed, string sdkRoot)
		{
			if (string.IsNullOrEmpty(sdkRoot))
				return installed;

			var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sdkRoot));
			var comparison = Util.IsWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

			var underRoot = installed
				.Where(s => s.Directory.Parent?.Parent is { } parent
					&& string.Equals(Path.TrimEndingDirectorySeparator(parent.FullName), root, comparison))
				.ToList();

			return underRoot.Count > 0 ? underRoot : installed;
		}

		/// <summary>The first line of <c>dotnet --version</c> output that is a version.</summary>
		internal static NuGetVersion ParseDotNetVersionOutput(IEnumerable<string> lines)
		{
			foreach (var line in lines ?? [])
			{
				if (NuGetVersion.TryParse(line?.Trim(), out var version))
					return version;
			}

			return null;
		}

		private static DotNetSdkInfo FindInstalled(IEnumerable<DotNetSdkInfo> installed, string version)
			=> NuGetVersion.TryParse(version.Trim(), out var parsed)
				? installed.FirstOrDefault(s => s.Version == parsed)
				: null;

		private static string Describe(IEnumerable<DotNetSdkInfo> installed)
			=> string.Join(", ", installed.OrderBy(s => s.Version).Select(s => s.Version.ToString()));

		private static string DescribeRequired(IEnumerable<Manifest.DotNetSdk> required)
			=> ".NET SDK " + string.Join(" or ", required.Select(r => r.Version));
	}
}
