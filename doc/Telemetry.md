---
uid: UnoCheck.Telemetry
---

# Telemetry

uno-check collects anonymized usage telemetry to help the Uno Platform team understand how the tool is used and identify areas for improvement. This document describes the telemetry events tracked by uno-check.

All events use the prefix `uno/uno-check` and are sent to Azure Application Insights.

## Implementation

The telemetry implementation can be found in the following files:
- [TelemetryClient.cs](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/Telemetry/TelemetryClient.cs) - Main telemetry client
- [Program.cs](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/Program.cs#L17) - Initialization
- [CheckCommand.cs](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/CheckCommand.cs) - Event tracking

## Tracked Events

The following table lists all telemetry events tracked by uno-check:

| Event Name | Source | Main Properties | Measurements | Description |
|------------|--------|-----------------|--------------|-------------|
| **check-start** | [CheckCommand.ExecuteAsync](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/CheckCommand.cs#L23) | RequestedFrameworks | - | Tracks when the check command starts. Frameworks are filtered to valid TFM format (netX.0 or netX.0-XXXX), limited to 32 chars each, max 10 frameworks |
| **check-success** | [CheckCommand.ExecuteAsync](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/CheckCommand.cs#L370) | - | Duration (seconds) | Tracks successful completion of all checks with no errors |
| **check-warn** | [CheckCommand.ExecuteAsync](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/CheckCommand.cs#L358) | ReportedChecks (comma-separated) | Duration (seconds) | Tracks check completion with warnings. ReportedChecks contains IDs of checkups that raised warnings |
| **check-fail** | [CheckCommand.ExecuteAsync](https://github.com/unoplatform/uno.check/blob/main/UnoCheck/CheckCommand.cs#L343) | ReportedChecks (comma-separated, "~" prefix for skipped fixes) | Duration (seconds) | Tracks check failure. ReportedChecks contains IDs of failed checkups, with "~" prefix if the fix was skipped by user |

## Event Flow

The telemetry system follows this lifecycle:

1. **Initialization** - `TelemetryClient.Init()` is called in `Program.Main()` before any other operations
2. **Start Tracking** - `TrackStartCheck()` called at beginning of CheckCommand with requested frameworks
3. **Completion Tracking** - One of the following is called based on results:
   - `TrackCheckSuccess()` - All checks passed with no errors or warnings
   - `TrackCheckWarning()` - Some checks completed with warnings
   - `TrackCheckFail()` - One or more checks failed
4. **Telemetry Submission** - Events are sent to Application Insights asynchronously

## Property and Measurement Details

### RequestedFrameworks (check-start event)

The `RequestedFrameworks` property contains a comma-separated list of target framework monikers (TFMs) specified by the user.

**Processing rules:**
- Frameworks are validated using regex pattern: `^net\d+(\.0)(?:-[a-zA-Z0-9.]+)*$`
- Each framework is limited to 32 characters
- Maximum of 10 frameworks are included
- Frameworks are sorted alphabetically
- Empty string if no frameworks specified

**Examples:**
```
""                                    # No frameworks specified
"net8.0"                              # Single framework
"net8.0,net8.0-android,net8.0-ios"   # Multiple frameworks
```

### ReportedChecks (check-warn and check-fail events)

The `ReportedChecks` property contains a comma-separated list of checkup IDs that reported warnings or failures.

**For check-warn:**
- Contains IDs of checkups that completed with warnings
- Example: `"openjdk,androidsdk,vswin"`

**For check-fail:**
- Contains IDs of failed checkups
- Prefixed with "~" if user skipped the fix
- Example: `"~openjdk,androidsdk"` (openjdk fix was skipped, androidsdk fix was not)

### Duration (completion events)

The `Duration` measurement tracks the total elapsed time in seconds (as a double).

**Examples:**
```
45.3    # 45.3 seconds
120.0   # 2 minutes
0.5     # Half a second
```

## Instrumentation Keys

uno-check uses different Application Insights instrumentation keys depending on the build configuration:

- **Production** (Release builds): `9a44058e-1913-4721-a979-9582ab8bedce`
- **Development** (Debug builds): `81286976-e3a4-49fb-b03b-30315092dbc4`

This separation allows the team to distinguish between production usage and development/testing activity.

## Error Handling

All telemetry operations are designed to fail silently:

- Every telemetry call is wrapped in a try-catch block
- Exceptions are caught and suppressed
- In Debug builds, telemetry failures are logged to console
- Telemetry failures never impact the tool's functionality
- The tool continues to operate normally even if telemetry fails

This ensures that telemetry issues never prevent users from using uno-check.

## Commands Without Telemetry

The following commands do **not** currently emit telemetry events:

- **list** - Lists available checkups
- **config** - Configures global.json and NuGet.config

Only the `check` command (the default command when uno-check is run without arguments) emits telemetry events.

## Privacy and Data Collection

uno-check uses the `Uno.DevTools.Telemetry` package for telemetry collection. The data collected is:

- **Anonymous** - No personally identifiable information (PII) is collected
- **Aggregated** - Used to understand usage patterns and improve the tool
- **Limited** - Only the events and properties described in this document are sent

The telemetry helps the Uno Platform team:
- Understand which platforms and frameworks developers are targeting
- Identify common failures and areas for improvement
- Measure the effectiveness of automatic fixes
- Prioritize development efforts based on actual usage patterns
