namespace DotNetCheck;

/// <summary>
/// uno-check's telemetry contract: the event names, the property names and the fixed values. Dashboards
/// query these by name, so changing one breaks them.
/// </summary>
internal static class TelemetryNames
{
	internal const string CheckStart = "check-start";
	internal const string CheckSuccess = "check-success";
	internal const string CheckWarn = "check-warn";
	internal const string CheckFail = "check-fail";

	internal const string RequestedFrameworks = "RequestedFrameworks";
	internal const string ReportedChecks = "ReportedChecks";
	internal const string Duration = "Duration";

	// What launched the run, and with which options (uno.check#643). One property per option, so a new one
	// can be added later without changing the values of the others.
	internal const string Caller = "Caller";

	/// <summary>
	/// The <c>--ide</c> option. Not <c>IDE</c>: other Uno Platform telemetry already uses that name for the
	/// IDE and its version, and a key that differs only by case reads as a different column.
	/// </summary>
	internal const string IdeOption = "IdeOption";

	internal const string Targets = "Targets";
	internal const string NonInteractive = "NonInteractive";
	internal const string StructuredOutput = "StructuredOutput";
	internal const string Fix = "Fix";

	/// <summary>The <c>--ci</c> option, as distinct from the CI the telemetry library detects on its own.</summary>
	internal const string CIOption = "CIOption";

	internal const string True = "true";
	internal const string False = "false";

	/// <summary>The run could not be described. Only used when describing it failed.</summary>
	internal const string Unknown = "unknown";

	/// <summary>
	/// Nobody named a caller or an IDE: a person in a terminal, or a host that passes neither. Deliberately not
	/// <c>cli</c>, because a host that passes nothing would then be counted as a person.
	/// </summary>
	internal const string None = "none";

	/// <summary>A value was given but is not one uno-check records. Sent instead of the value itself.</summary>
	internal const string Other = "other";

	/// <summary>The run checks every platform.</summary>
	internal const string AllTargets = "all";
}
