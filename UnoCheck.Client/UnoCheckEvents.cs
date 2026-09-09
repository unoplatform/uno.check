#nullable enable

using System;

namespace Uno.Check.Client;

/// <summary>
/// Caller-tunable run scope, mirroring the CLI flags: <c>--target</c> per platform id,
/// <c>--skip</c> per excluded checkup id, <c>--pre</c> for the preview channel,
/// <c>--verbose</c> for verbose output.
/// </summary>
/// <param name="TargetPlatforms">Platform ids to scope the run to; empty means every applicable checkup.</param>
/// <param name="SkipCheckupIds">Checkup ids to exclude. Never forwarded to a fix.</param>
/// <param name="PreviewChannel">Use the preview manifest channel (<c>--pre</c>).</param>
/// <param name="Verbose">Ask the tool for verbose output (<c>--verbose</c>).</param>
/// <param name="ApplyFixes">Apply fixes as the run streams (<c>--fix</c>).</param>
public sealed record UnoCheckRunOptions(
	string[] TargetPlatforms,
	string[] SkipCheckupIds,
	bool PreviewChannel,
	bool Verbose,
	bool ApplyFixes = false)
{
	/// <summary>Every applicable checkup, stable channel, no fixes.</summary>
	public static UnoCheckRunOptions Default { get; } = new(
		Array.Empty<string>(), Array.Empty<string>(), PreviewChannel: false, Verbose: false);
}

/// <summary>One entry of the checkup catalog (<c>list --json</c>).</summary>
/// <param name="Id">Checkup id, as <c>--only</c> and <c>--skip</c> accept it.</param>
/// <param name="Title">Human-readable name.</param>
public sealed record UnoCheckCatalogEntry(string Id, string Title);

/// <summary>An event surfaced from the JSONL stream.</summary>
public abstract record UnoCheckEvent;

/// <summary>The run has started and announced its scope.</summary>
/// <param name="CheckupCount">
/// How many checkups this run will examine. A scoped run (<c>--only</c>) reports only its own
/// scope, so a host tracking overall progress must not let a scoped run overwrite the total.
/// </param>
public sealed record UnoCheckRunStarted(int CheckupCount) : UnoCheckEvent;

/// <summary>A checkup has begun examining.</summary>
/// <param name="Id">Checkup id.</param>
/// <param name="Name">Human-readable name.</param>
public sealed record UnoCheckCheckStarted(string Id, string Name) : UnoCheckEvent;

/// <summary>Progress text from a checkup still examining.</summary>
/// <param name="Id">Checkup id.</param>
/// <param name="Message">Progress text intended for display.</param>
public sealed record UnoCheckCheckProgress(string Id, string Message) : UnoCheckEvent;

/// <summary>A checkup's final state — re-emitted after a fix, so the last result per id wins.</summary>
/// <param name="Id">Checkup id.</param>
/// <param name="Name">Human-readable name.</param>
/// <param name="Status">One of <c>ok</c>, <c>warning</c>, <c>error</c>, <c>skipped</c>.</param>
/// <param name="Message">Detail for display, when the checkup provided one.</param>
/// <param name="SkipReason">Why the checkup was skipped, when it was.</param>
/// <param name="Fixable">Whether the contract reported an automatic fix for this result.</param>
/// <param name="RequiresElevation">
/// Whether applying this fix needs admin/root (<c>fix.requires_elevation</c>). Defaults to
/// true when the engine does not report it: Windows elevates whole processes, so a host must
/// decide before launching, and guessing "unelevated" would fail the fix outright.
/// </param>
public sealed record UnoCheckCheckResult(
	string Id,
	string Name,
	string Status,
	string? Message,
	string? SkipReason,
	bool Fixable,
	bool RequiresElevation = true) : UnoCheckEvent;

/// <summary>A fix has begun applying.</summary>
/// <param name="Id">Checkup id being fixed.</param>
/// <param name="Solution">Name of the solution being applied, when reported.</param>
public sealed record UnoCheckFixStarted(string Id, string? Solution) : UnoCheckEvent;

/// <summary>Progress text from a fix in flight.</summary>
/// <param name="Id">Checkup id being fixed.</param>
/// <param name="Message">Progress text intended for display.</param>
public sealed record UnoCheckFixProgress(string Id, string Message) : UnoCheckEvent;

/// <summary>The outcome of one fix.</summary>
/// <param name="Id">Checkup id that was fixed.</param>
/// <param name="Success">Whether the fix applied cleanly.</param>
/// <param name="Error">Failure detail, when it did not.</param>
public sealed record UnoCheckFixResult(string Id, bool Success, string? Error) : UnoCheckEvent;

/// <summary>The guaranteed end-of-stream marker, emitted on every exit path.</summary>
/// <param name="Status">Overall run status, <c>healthy</c> or <c>unhealthy</c>.</param>
/// <param name="Reason">
/// Why the run could not complete — for example an <c>--only</c> id that matched no checkup,
/// which fails the run rather than passing empty. Null on a run that completed normally.
/// </param>
/// <param name="Total">Checkups examined.</param>
/// <param name="Ok">Checkups that passed.</param>
/// <param name="Warning">Checkups that warned.</param>
/// <param name="Error">Checkups that failed.</param>
/// <param name="Skipped">Checkups skipped.</param>
public sealed record UnoCheckRunCompleted(
	string Status,
	string? Reason,
	int Total,
	int Ok,
	int Warning,
	int Error,
	int Skipped) : UnoCheckEvent;
