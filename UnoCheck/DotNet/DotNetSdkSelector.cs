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

		/// <summary>The newest installed SDK that fits an SDK the manifest requires.</summary>
		Manifest,

		/// <summary>No installed SDK fits the manifest, so the newest one.</summary>
		Newest,
	}

	/// <summary>
	/// What the dotnet host resolved for a <c>global.json</c> that pins an SDK: the version, or why it could not.
	/// </summary>
	internal sealed record GlobalJsonSdkResolution(string Path, NuGetVersion Version, string Failure);

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

			return Select(installed, required, requestedVersion, globalJson);
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
					warnings.Add($"global.json '{globalJson.Path}' did not resolve to an installed .NET SDK ({globalJson.Failure}). It is ignored.");
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
						+ "Update global.json to the .NET version of the manifest, or use a manifest that targets the .NET version of global.json (--manifest).");
				}
				else
				{
					return new DotNetSdkSelection(pinned, DotNetSdkSelectionSource.GlobalJson, warnings, GlobalJsonPath: globalJson.Path);
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
		/// when it pins an SDK with <c>sdk.version</c>; otherwise null.
		/// </summary>
		internal static string FindGlobalJsonPin(string startDirectory)
		{
			for (var directory = string.IsNullOrEmpty(startDirectory) ? null : new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
			{
				var path = Path.Combine(directory.FullName, "global.json");

				if (!File.Exists(path))
					continue;

				return HasSdkVersion(path) ? path : null;
			}

			return null;
		}

		private static bool HasSdkVersion(string path)
		{
			try
			{
				using var document = JsonDocument.Parse(
					File.ReadAllText(path),
					new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });

				return document.RootElement.ValueKind == JsonValueKind.Object
					&& document.RootElement.TryGetProperty("sdk", out var sdk)
					&& sdk.ValueKind == JsonValueKind.Object
					&& sdk.TryGetProperty("version", out var version)
					&& version.ValueKind == JsonValueKind.String
					&& !string.IsNullOrWhiteSpace(version.GetString());
			}
			catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
			{
				Util.Log($"Ignoring unreadable global.json '{path}': {ex.Message}");
				return false;
			}
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
