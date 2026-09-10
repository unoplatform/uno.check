#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Uno.Check.Client;

/// <summary>
/// The pure half of driving uno-check: command-line construction and JSONL event mapping,
/// with no process launching. Everything here is deterministic and side-effect free, so a
/// host can unit-test what it is about to execute — which matters because these strings end
/// up on a command line that is sometimes elevated.
///
/// Launch policy deliberately lives with the host, not here: whether to invoke the installed
/// global tool or <c>dnx</c>, which version to pin, and how to elevate are deployment
/// decisions that differ per host.
/// </summary>
public static class UnoCheckProtocol
{
	/// <summary>
	/// Ids flow into a host-executed (often elevated) command line; only ids that cannot
	/// smuggle argument or shell metacharacters are accepted, mirroring the CLI's allow-list.
	/// </summary>
	public static bool IsSafeCheckupId(string? id)
	{
		if (string.IsNullOrEmpty(id))
		{
			return false;
		}

		foreach (var c in id!)
		{
			var isAsciiLetterOrDigit = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9');
			if (!isAsciiLetterOrDigit && c != '.' && c != '_' && c != '-')
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// Whether a value can be embedded in a hand-quoted command line. Some elevation APIs
	/// take a single string rather than an argument list, where an embedded quote would break
	/// the argument boundary. Paths with spaces stay fine — only quotes are refused.
	/// </summary>
	public static bool IsSafeEngineArgument(string? value)
		=> !string.IsNullOrWhiteSpace(value) && value!.IndexOf('"') < 0;

	/// <summary>
	/// Tool-side args for a diagnosis. <paramref name="allowElevationPrompt"/> is the host's
	/// platform decision: off on Windows, where elevation means launching a separate elevated
	/// child, and on elsewhere so a protected command can raise the platform's own
	/// authorization dialog.
	/// </summary>
	public static string[] BuildDiagnosisArguments(UnoCheckRunOptions options, bool allowElevationPrompt)
	{
		var arguments = new List<string> { "--non-interactive", "--json" };

		AppendRepeated(arguments, "--target", options.TargetPlatforms);
		AppendRepeated(arguments, "--skip", options.SkipCheckupIds);

		if (options.PreviewChannel)
		{
			arguments.Add("--pre");
		}

		if (options.Verbose)
		{
			arguments.Add("--verbose");
		}

		if (options.ApplyFixes)
		{
			arguments.Add("--fix");
			if (allowElevationPrompt)
			{
				arguments.Add("--allow-elevation-prompt");
			}
		}

		return arguments.ToArray();
	}

	/// <summary>
	/// Tool-side args for fixing one or more checkups in a single invocation. The fix runs
	/// against the same target/channel scope as the diagnosis that surfaced it — never a
	/// different manifest. Skip ids are deliberately not forwarded: a fix must not be filtered
	/// out by whatever the host chose to display.
	///
	/// Every id is named with its own <c>--only</c>, which is what makes one batch — and so a
	/// single elevation prompt — possible: the CLI fixes exactly the named ids, examining
	/// their dependencies without fixing them. Ids must come from the catalog or a prior
	/// report and never be constructed: several embed a version
	/// (<c>dotnetworkloads-&lt;sdk band&gt;</c>) and caller ids match exactly, so a constructed
	/// one fails the whole run with an "unknown checkup id(s)" reason.
	///
	/// A <paramref name="jsonFilePath"/> switches the transport to a file, for an elevated
	/// child whose stdout cannot cross the privilege boundary.
	/// </summary>
	public static string[] BuildFixArguments(
		IReadOnlyList<string> checkupIds,
		UnoCheckRunOptions options,
		bool allowElevationPrompt,
		string? jsonFilePath)
	{
		var arguments = new List<string> { "--fix", "--non-interactive" };

		AppendRepeated(arguments, "--only", checkupIds);
		AppendRepeated(arguments, "--target", options.TargetPlatforms);

		if (options.PreviewChannel)
		{
			arguments.Add("--pre");
		}

		if (jsonFilePath is null)
		{
			arguments.Add("--json");
			if (allowElevationPrompt)
			{
				arguments.Add("--allow-elevation-prompt");
			}
		}
		else
		{
			arguments.Add("--json-file");
			arguments.Add(jsonFilePath);
		}

		return arguments.ToArray();
	}

	/// <summary>Tool-side args for the checkup catalog (<c>list --json</c>, honors targets/channel).</summary>
	public static string[] BuildCatalogArguments(UnoCheckRunOptions options)
	{
		var arguments = new List<string> { "list", "--json" };

		AppendRepeated(arguments, "--target", options.TargetPlatforms);

		if (options.PreviewChannel)
		{
			arguments.Add("--pre");
		}

		return arguments.ToArray();
	}

	/// <summary>
	/// Re-examines specific checkups without fixing. One invocation names every id: process
	/// startup is a fixed tax, so splitting a subset across processes pays it once per checkup.
	/// </summary>
	public static string[] BuildScopedCheckArguments(IReadOnlyList<string> checkupIds, UnoCheckRunOptions options)
	{
		var arguments = new List<string>();
		AppendRepeated(arguments, "--only", checkupIds);
		arguments.AddRange(BuildDiagnosisArguments(
			options with { ApplyFixes = false, SkipCheckupIds = Array.Empty<string>() },
			allowElevationPrompt: false));
		return arguments.ToArray();
	}

	private static void AppendRepeated(List<string> arguments, string flag, IReadOnlyList<string> values)
	{
		foreach (var value in values)
		{
			if (IsSafeCheckupId(value))
			{
				arguments.Add(flag);
				arguments.Add(value);
			}
		}
	}

	/// <summary>Deserializes one JSONL line; null when the line is not valid JSON.</summary>
	public static UnoCheckEventLine? TryParseLine(string line)
	{
		try
		{
#if NET8_0_OR_GREATER
			return JsonSerializer.Deserialize(line, UnoCheckJsonContext.Default.UnoCheckEventLine);
#else
			return JsonSerializer.Deserialize<UnoCheckEventLine>(line);
#endif
		}
		catch (JsonException)
		{
			return null;
		}
	}

	/// <summary>Maps a parsed line to an event; null when the line is not one a host renders.</summary>
	public static UnoCheckEvent? Map(UnoCheckEventLine line)
	{
		switch (line.Type)
		{
			case "run_started":
				return new UnoCheckRunStarted(line.CheckupCount);

			case "checkup_started":
				return new UnoCheckCheckStarted(line.Id ?? "", line.Name ?? "");

			case "checkup_progress":
				return string.IsNullOrEmpty(line.Message)
					? null
					: new UnoCheckCheckProgress(line.Id ?? "", line.Message!);

			case "checkup_result":
				if (line.Check is not { } check)
				{
					return null;
				}

				return new UnoCheckCheckResult(
					check.Id ?? "",
					check.Name ?? "",
					check.Status ?? "error",
					check.Message,
					check.SkipReason,
					check.Fix?.AutoFixable == true,
					// No signal from an engine predating the field means elevate anyway.
					check.Fix?.RequiresElevation ?? true);

			case "fix_started":
				return new UnoCheckFixStarted(line.Id ?? "", line.Solution);

			case "fix_progress":
				return string.IsNullOrEmpty(line.Message)
					? null
					: new UnoCheckFixProgress(line.Id ?? "", line.Message!);

			case "fix_result":
				return new UnoCheckFixResult(line.Id ?? "", line.Success, line.Error);

			case "report":
				if (line.Report is not { } report || report.Summary is not { } summary)
				{
					return null;
				}

				return new UnoCheckRunCompleted(
					report.Status ?? "unhealthy",
					report.Reason,
					summary.Total,
					summary.Ok,
					summary.Warning,
					summary.Error,
					summary.Skipped);

			default:
				return null;
		}
	}

	/// <summary>
	/// Catalog entries from a parsed <c>checkup_catalog</c> line. Schema 1.0 carries the human
	/// title in <c>name</c>; <c>title</c> is kept as a fallback for earlier contract drafts.
	/// </summary>
	public static IReadOnlyList<UnoCheckCatalogEntry> MapCatalog(UnoCheckEventLine line)
	{
		var entries = new List<UnoCheckCatalogEntry>();

		if (line.Type != "checkup_catalog" || line.Checkups is null)
		{
			return entries;
		}

		foreach (var checkup in line.Checkups)
		{
			if (!string.IsNullOrEmpty(checkup.Id))
			{
				entries.Add(new UnoCheckCatalogEntry(checkup.Id!, checkup.Name ?? checkup.Title ?? checkup.Id!));
			}
		}

		return entries;
	}
}
