#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DotNetCheck;

/// <summary>
/// What launched a check run, and with which options. It is recorded on <c>check-start</c> and on the
/// outcome events, so that a host's background run can be told apart from a run a developer typed
/// (uno.check#643). Without it, a host that checks every platform and fixes nothing fails on most
/// machines without mobile tooling, and those failures are indistinguishable from a developer's own.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IdeOption"/>, <see cref="Targets"/> and the flags only ever take values from fixed lists.
/// <see cref="Caller"/> is the one free-form value. It is lowercased, then kept only when it is a short name
/// that starts with an ASCII letter or digit and continues with ASCII letters, digits, '.', '_' and '-', and
/// is not one of the values uno-check uses itself (<c>none</c>, <c>other</c>, <c>unknown</c>); anything else is
/// recorded as <c>other</c>. So a path, an
/// email or a sentence never reaches telemetry, but a short name does, exactly as given. That is why hosts
/// must pass a product name, never a user, machine or organisation name, and the documentation says so.
/// </para>
/// <para>
/// Build it from settings that <see cref="Cli.CheckCommand.ApplyImpliedSettings"/> has settled, so it describes
/// the run that actually happens: <c>--tfm</c> has decided the platforms, and <c>--json</c>,
/// <c>--json-file</c> and <c>--ci</c> have made the run non-interactive.
/// </para>
/// </remarks>
internal sealed record CheckRunTelemetryContext(
	string Caller,
	string IdeOption,
	string Targets,
	string NonInteractive,
	string StructuredOutput,
	string Fix,
	string CIOption)
{
	/// <summary>Read when <c>--caller</c> is not given, for hosts that set the environment rather than the arguments.</summary>
	internal const string CallerEnvironmentVariable = "UNO_CHECK_CALLER";

	/// <summary>Long enough for a product name, short enough to keep the column tidy.</summary>
	internal const int MaxCallerLength = 32;

	private static readonly Regex SafeCaller = new(@"^[a-z0-9][a-z0-9._-]*$", RegexOptions.CultureInvariant);

	/// <summary>Sent when the run could not be described. Every value says so rather than guessing.</summary>
	internal static CheckRunTelemetryContext Unknown { get; } = new(
		TelemetryNames.Unknown,
		TelemetryNames.Unknown,
		TelemetryNames.Unknown,
		TelemetryNames.Unknown,
		TelemetryNames.Unknown,
		TelemetryNames.Unknown,
		TelemetryNames.Unknown);

	internal static CheckRunTelemetryContext From(CheckSettings settings, Func<string, string?>? getEnvironmentVariable)
	{
		if (settings is null)
		{
			return Unknown;
		}

		try
		{
			return new(
				ResolveCaller(settings.Caller, getEnvironmentVariable?.Invoke(CallerEnvironmentVariable)),
				DescribeIde(settings.Ide),
				DescribeTargets(settings.TargetPlatforms),
				Flag(settings.NonInteractive),
				Flag(settings.Json || !string.IsNullOrEmpty(settings.JsonFile)),
				Flag(settings.Fix),
				Flag(settings.CI));
		}
		catch (Exception ex) when (!IsFatal(ex))
		{
			// Describing the run must never stop it: the same rule TelemetryClient applies to sending.
			return Unknown;
		}
	}

	/// <summary>The <c>--caller</c> option wins; the environment variable is the fallback.</summary>
	internal static string ResolveCaller(string? option, string? environment)
		=> NormalizeCaller(string.IsNullOrWhiteSpace(option) ? environment : option);

	internal static string NormalizeCaller(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return TelemetryNames.None;
		}

		var name = value!.Trim().ToLowerInvariant();
		if (name.Length > MaxCallerLength || !SafeCaller.IsMatch(name) || IsReservedValue(name))
		{
			return TelemetryNames.Other;
		}

		return name;
	}

	/// <summary>
	/// A caller can't take a value uno-check records for its own reasons: <c>--caller none</c> would read as
	/// "nobody said", and wouldn't be replayed to fix runs.
	/// </summary>
	private static bool IsReservedValue(string name)
		=> name is TelemetryNames.None or TelemetryNames.Other or TelemetryNames.Unknown;

	/// <summary>Whether a normalised caller names a host, and so is worth passing on to a fix run.</summary>
	internal static bool IsReplayable(string caller)
		=> !IsReservedValue(caller);

	internal static string DescribeIde(string? value)
	{
		var ide = KnownIdes.Normalize(value);
		if (ide is null)
		{
			return TelemetryNames.None;
		}

		return KnownIdes.IsKnown(ide) ? ide : TelemetryNames.Other;
	}

	/// <summary>
	/// The platforms the run checks, as canonical lowercase names, de-duplicated and sorted, so
	/// <c>--target wasm --target web</c> and <c>--target webassembly</c> read the same. None, or any
	/// <c>all</c>, means every platform. A value the run doesn't recognise is recorded as <c>other</c>. Values
	/// are read exactly as the run reads them (<see cref="TargetPlatformHelper.GetTargetPlatformFromFlag"/>,
	/// untrimmed), so <c>--target " ios"</c>, which checks no iOS, never reads as <c>ios</c>.
	/// </summary>
	internal static string DescribeTargets(string[]? targetPlatforms)
	{
		if (targetPlatforms is not { Length: > 0 })
		{
			return TelemetryNames.AllTargets;
		}

		var platforms = targetPlatforms
			.Select(flag => flag is null ? TargetPlatform.None : TargetPlatformHelper.GetTargetPlatformFromFlag(flag))
			.ToList();

		if (platforms.Contains(TargetPlatform.All))
		{
			return TelemetryNames.AllTargets;
		}

		var names = new SortedSet<string>(
			platforms.Select(platform => platform == TargetPlatform.None ? TelemetryNames.Other : platform.ToString().ToLowerInvariant()),
			StringComparer.Ordinal);

		return string.Join(",", names);
	}

	internal (string Key, string Value)[] ToProperties() =>
	[
		(TelemetryNames.Caller, Caller),
		(TelemetryNames.IdeOption, IdeOption),
		(TelemetryNames.Targets, Targets),
		(TelemetryNames.NonInteractive, NonInteractive),
		(TelemetryNames.StructuredOutput, StructuredOutput),
		(TelemetryNames.Fix, Fix),
		(TelemetryNames.CIOption, CIOption),
	];

	private static string Flag(bool value) => value ? TelemetryNames.True : TelemetryNames.False;

	/// <summary>Failures no catch should swallow: the process is in no state to carry on.</summary>
	private static bool IsFatal(Exception ex)
		=> ex is OutOfMemoryException or StackOverflowException or AccessViolationException;
}
