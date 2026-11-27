#nullable enable
using System;

namespace DotNetCheck;

/// <summary>
/// The <c>--ide</c> values uno-check acts on, and the checkups each one makes unnecessary. The run and its
/// telemetry both read this one list, so an IDE added here is both acted on and recorded.
/// </summary>
internal static class KnownIdes
{
	internal const string Rider = "rider";
	internal const string VisualStudio = "vs";
	internal const string VisualStudioCode = "vscode";

	/// <summary>Trimmed and lowercased, or null when no IDE was given.</summary>
	internal static string? Normalize(string? ide)
		=> string.IsNullOrWhiteSpace(ide) ? null : ide!.Trim().ToLowerInvariant();

	internal static bool IsKnown(string? ide)
		=> Normalize(ide) is Rider or VisualStudio or VisualStudioCode;

	internal static string[] SkipsFor(string? ide) => Normalize(ide) switch
	{
		Rider => Util.RiderSkips,
		VisualStudio => Util.VSSkips,
		VisualStudioCode => Util.VSCodeSkips,
		_ => Array.Empty<string>(),
	};
}
