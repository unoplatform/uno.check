using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Uno.DevTools.Telemetry;

namespace DotNetCheck;

internal static class TelemetryClient
{
    private const string InstrumentationKey = "9a44058e-1913-4721-a979-9582ab8bedce";

    private static Telemetry _telemetry;

    public static void Init()
    {
        try
        {
            _telemetry = new Telemetry(InstrumentationKey, "uno-check", typeof(TelemetryClient).Assembly);
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackStartCheck(string[] requestedFrameworks)
    {
        try
        {
            // Remove strings that are not of the format net8.0 or net8.0-XXXXX, using a regex
            var frameworks = string.Join(
                ",",
                requestedFrameworks
                    .OrderBy(s => s)
                    .Where(f => System.Text.RegularExpressions.Regex.IsMatch(f, @"^net\d+(\.0)(?:-[a-zA-Z0-9.]+)*$"))
                    .Select(s => s[..32])
                    .Take(10));

            _telemetry.TrackEvent(
                "check-start",
                [
                    ("RequestedFrameworks", frameworks),
                ],
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

    public static void TrackCheckSuccess(TimeSpan elapsed)
    {
        try
        {
            _telemetry.TrackEvent(
                "check-success",
                [],
                [
                    ("Duration", elapsed.TotalSeconds)
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

    public static void TrackCheckWarning(TimeSpan elapsed, string warnChecks)
    {
        try
        {
            _telemetry.TrackEvent(
                "check-warn",
                [
                    ("ReportedChecks", warnChecks),
                ],
                [
                    ("Duration", elapsed.TotalSeconds)
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

    public static void TrackCheckFail(TimeSpan elapsed, string failedChecks)
    {
        try
        {
            _telemetry.TrackEvent(
                "check-fail",
                [
                    ("ReportedChecks", failedChecks),
                ],
                [
                    ("Duration", elapsed.TotalSeconds)
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
}
