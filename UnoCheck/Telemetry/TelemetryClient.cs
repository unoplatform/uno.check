using Newtonsoft.Json;
using System;
using System.Collections.Generic;
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
            _telemetry = new Telemetry(InstrumentationKey);
        }
        catch (Exception e)
        {
#if DEBUG
            Console.WriteLine($"Telemetry failure: {e}");
#endif
        }
    }

    public static void TrackStartCheck()
    {
        try
        {
            _telemetry.TrackEvent(
                "unocheck-check-start",
                [],
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
                "unocheck-check-success",
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
                "unocheck-check-warn",
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
                "unocheck-check-fail",
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
