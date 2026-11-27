using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Uno.DevTools.Telemetry;

namespace DotNetCheck;

internal static class TelemetryClient
{
    private const string InstrumentationKey =
#if !DEBUG
		"9a44058e-1913-4721-a979-9582ab8bedce"; // Production key
#else
		"81286976-e3a4-49fb-b03b-30315092dbc4"; // Dev key
#endif

    private static Telemetry _telemetry;

    public static void Init()
    {
        try
        {
            _telemetry = new Telemetry(InstrumentationKey, "uno/uno-check", typeof(TelemetryClient).Assembly);
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackStartCheck(string[] requestedFrameworks, CheckRunTelemetryContext context)
    {
        try
        {
            // Remove strings that are not of the format net8.0 or net8.0-XXXXX, using a regex
            var frameworks = string.Join(
                ",",
                requestedFrameworks?
                    .OrderBy(s => s)
                    .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^net\d+(\.0)(?:-[a-zA-Z0-9.]+)*$"))
                    .Select(s => s.Length >= 32 ? s[..32] : s)
                    .Take(10) ?? []);

            _telemetry.TrackEvent(
                TelemetryNames.CheckStart,
                BuildProperties(context, (TelemetryNames.RequestedFrameworks, frameworks)),
                []
            );
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackCheckSuccess(TimeSpan elapsed, CheckRunTelemetryContext context)
    {
        try
        {
            _telemetry.TrackEvent(
                TelemetryNames.CheckSuccess,
                BuildProperties(context),
                [
                    (TelemetryNames.Duration, elapsed.TotalSeconds)
                ]
            );
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackCheckWarning(TimeSpan elapsed, string warnChecks, CheckRunTelemetryContext context)
    {
        try
        {
            _telemetry.TrackEvent(
                TelemetryNames.CheckWarn,
                BuildProperties(context, (TelemetryNames.ReportedChecks, warnChecks)),
                [
                    (TelemetryNames.Duration, elapsed.TotalSeconds)
                ]
            );
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackCheckFail(TimeSpan elapsed, string failedChecks, CheckRunTelemetryContext context)
    {
        try
        {
            _telemetry.TrackEvent(
                TelemetryNames.CheckFail,
                BuildProperties(context, (TelemetryNames.ReportedChecks, failedChecks)),
                [
                    (TelemetryNames.Duration, elapsed.TotalSeconds)
                ]
            );
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    /// <summary>
    /// An event's own properties, followed by the run's context (uno.check#643). A context key the event
    /// already sets is left out: the library turns the list into a dictionary, so a repeated key would
    /// throw there, and the catch above would drop the whole event without a trace.
    /// </summary>
    internal static (string Key, string Value)[] BuildProperties(CheckRunTelemetryContext context, params (string Key, string Value)[] eventProperties)
    {
        var properties = new List<(string Key, string Value)>(eventProperties);
        properties.AddRange(
            (context ?? CheckRunTelemetryContext.Unknown).ToProperties()
                .Where(property => !eventProperties.Any(p => string.Equals(p.Key, property.Key, StringComparison.Ordinal))));

        return properties.ToArray();
    }
}
