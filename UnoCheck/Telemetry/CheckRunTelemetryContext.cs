#nullable enable
using System;
using System.Collections.Generic;
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
/// made of letters, digits, '.', '_' and '-'; anything else is recorded as <c>other</c>. So a path, an
/// email or a sentence never reaches telemetry, but a short name does, exactly as given. That is why hosts
/// must pass a product name, never a user, machine or organisation name, and the documentation says so.
/// </para>
/// <para>
/// Build it from settings that <see cref="CheckCommand.ApplyImpliedSettings"/> has settled, so it describes
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
		catch (Exception)
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
		return name.Length <= MaxCallerLength && SafeCaller.IsMatch(name) ? name : TelemetryNames.Other;
	}

	/// <summary>Whether a normalised caller names a host, and so is worth passing on to a fix run.</summary>
	internal static bool IsReplayable(string caller)
		=> caller is not (TelemetryNames.None or TelemetryNames.Other or TelemetryNames.Unknown);

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
	/// <c>all</c>, means every platform.
	/// </summary>
	internal static string DescribeTargets(string[]? targetPlatforms)
	{
		if (targetPlatforms is not { Length: > 0 })
		{
			return TelemetryNames.AllTargets;
		}

		var names = new SortedSet<string>(StringComparer.Ordinal);
		foreach (var flag in targetPlatforms)
		{
			var platform = string.IsNullOrWhiteSpace(flag)
				? TargetPlatform.None
				: TargetPlatformHelper.GetTargetPlatformFromFlag(flag.Trim());

			if (platform == TargetPlatform.All)
			{
				return TelemetryNames.AllTargets;
			}

			names.Add(platform == TargetPlatform.None ? TelemetryNames.Unknown : platform.ToString().ToLowerInvariant());
		}

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
}
