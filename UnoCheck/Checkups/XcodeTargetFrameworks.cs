using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;
using NuGet.Versioning;

namespace DotNetCheck.Checkups
{
	/// <summary>
	/// Works out which Xcode the <c>--tfm</c> Apple target frameworks need (issue #618). Each .NET for iOS, tvOS,
	/// Mac Catalyst or macOS SDK pack accepts one Xcode major.minor, older or newer. The .NET SDK builds its own
	/// target framework with its current packs, which the manifest's Xcode matches. It builds an older one
	/// (<c>net10.0-ios</c> on the .NET 11 SDK) with that framework's pack, which needs an older Xcode.
	/// </summary>
	internal static class XcodeTargetFrameworks
	{
		static readonly string[] ApplePlatforms = ["ios", "tvos", "maccatalyst", "macos"];

		/// <summary>
		/// Splits the Apple target frameworks into those built with the manifest's Xcode, and those whose .NET version
		/// is in <paramref name="olderXcodeVersions"/> (the manifest's <c>xcode.targetFrameworks</c>). Other target
		/// frameworks, and entries whose Xcode version doesn't parse, are ignored.
		/// </summary>
		internal static XcodeTargets Classify(IEnumerable<string> targetFrameworks, IReadOnlyDictionary<string, string> olderXcodeVersions)
		{
			var current = new List<string>();
			var older = new List<OlderTarget>();

			var appleTargets = (targetFrameworks ?? [])
				.Select(t => (IsApple: TryParseApple(t, out var name, out var dotNetVersion), Name: name, DotNetVersion: dotNetVersion))
				.Where(t => t.IsApple);

			foreach (var (_, name, dotNetVersion) in appleTargets)
			{
				if (olderXcodeVersions is not null
					&& olderXcodeVersions.TryGetValue(dotNetVersion, out var xcode)
					&& TryGetMajorMinor(xcode, out var majorMinor))
				{
					older.Add(new OlderTarget(name, dotNetVersion, majorMinor));
				}
				else
				{
					current.Add(name);
				}
			}

			return new XcodeTargets(current, older);
		}

		/// <summary>Whether the Xcode's major.minor is the required one, the comparison the Apple SDK packs make.</summary>
		internal static bool MatchesMajorMinor(NuGetVersion xcode, string required)
			=> xcode is not null
				&& NuGetVersion.TryParse(required, out var version)
				&& xcode.Major == version.Major
				&& xcode.Minor == version.Minor;

		/// <summary>The target framework for the same platform on another .NET version: net10.0-ios to net11.0-ios.</summary>
		internal static string Retarget(string targetFramework, string dotNetVersion)
		{
			var framework = NuGetFramework.ParseFolder(targetFramework.Trim());

			return framework.HasPlatform ? $"{dotNetVersion}-{framework.Platform.ToLowerInvariant()}" : dotNetVersion;
		}

		internal static bool TryGetMajorMinor(string version, out string majorMinor)
		{
			majorMinor = NuGetVersion.TryParse(version?.Trim(), out var parsed) ? $"{parsed.Major}.{parsed.Minor}" : null;

			return majorMinor is not null;
		}

		static bool TryParseApple(string targetFramework, out string name, out string dotNetVersion)
		{
			name = targetFramework?.Trim();
			dotNetVersion = null;

			if (string.IsNullOrEmpty(name))
				return false;

			var framework = NuGetFramework.ParseFolder(name);

			if (!framework.HasPlatform || !ApplePlatforms.Contains(framework.Platform, StringComparer.OrdinalIgnoreCase))
				return false;

			dotNetVersion = $"net{framework.Version.Major}.{framework.Version.Minor}";

			return true;
		}
	}

	/// <summary>An Apple target framework that needs an older Xcode: <c>net10.0-ios</c>, <c>net10.0</c>, <c>26.0</c>.</summary>
	internal sealed record OlderTarget(string TargetFramework, string DotNetVersion, string XcodeVersion);

	/// <summary>The Apple target frameworks from <c>--tfm</c>, split by the Xcode they need.</summary>
	internal sealed record XcodeTargets(IReadOnlyList<string> Current, IReadOnlyList<OlderTarget> Older)
	{
		public static readonly XcodeTargets None = new([], []);

		/// <summary>
		/// The Xcode major.minor to check when every Apple target framework is an older one and they all need the
		/// same Xcode. Null otherwise: the manifest's Xcode is checked, and <see cref="Uncovered"/> lists the rest.
		/// </summary>
		public string OlderOnlyXcodeVersion
			=> Current.Count == 0 && Older.Count > 0 && Older.All(o => o.XcodeVersion == Older[0].XcodeVersion)
				? Older[0].XcodeVersion
				: null;

		/// <summary>The older target frameworks that the checked Xcode can't build, when one Xcode can't build them all.</summary>
		public IReadOnlyList<OlderTarget> Uncovered
			=> OlderOnlyXcodeVersion is null ? Older : [];
	}
}
