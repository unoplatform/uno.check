#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DotNetCheck.Models;

namespace DotNetCheck.Reporting;

internal static class CheckReportFactory
{
    public static CheckReport Create(
        IDictionary<string, DiagnosticResult> results,
        IEnumerable<SkipInfo> skippedCheckups,
        IEnumerable<string> skippedFixes,
        CheckSettings settings,
        Manifest.Manifest manifest,
        ManifestChannel manifestChannel,
        DateTimeOffset startedAtUtc,
        TimeSpan duration,
        Platform platform,
        int exitCode,
        IDictionary<string, IReadOnlyList<CheckResultDetailReport>>? checkupDetails = null,
        IDictionary<string, string>? skippedFixReasons = null)
    {
        if (results is null)
        {
            throw new ArgumentNullException(nameof(results));
        }

        if (skippedCheckups is null)
        {
            throw new ArgumentNullException(nameof(skippedCheckups));
        }

        if (skippedFixes is null)
        {
            throw new ArgumentNullException(nameof(skippedFixes));
        }

        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        var completedAtUtc = startedAtUtc + duration;

        return new CheckReport(
            ToolInfo.CurrentVersion.ToNormalizedString(),
            manifest.Check?.ToolVersion ?? string.Empty,
            manifestChannel.ToString().ToLowerInvariant(),
            startedAtUtc,
            completedAtUtc,
            duration.TotalSeconds,
            exitCode,
            platform,
            settings.Frameworks ?? [],
            settings.TargetPlatforms ?? [],
            CreateCheckResultReports(results, checkupDetails),
            CreateSkippedCheckReports(skippedCheckups),
            skippedFixes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CreateUnresolvedCheckReports(results, skippedFixes, skippedFixReasons),
            results.Values.Any(r => r.Status == Status.Error),
            results.Values.Any(r => r.Status == Status.Warning));
    }

    private static IReadOnlyList<CheckResultReport> CreateCheckResultReports(
        IDictionary<string, DiagnosticResult> results,
        IDictionary<string, IReadOnlyList<CheckResultDetailReport>>? checkupDetails)
    {
        var reports = results.Values
            .Select(r => CreateCheckResultReport(r, checkupDetails))
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return reports;
    }

    private static CheckResultReport CreateCheckResultReport(
        DiagnosticResult result,
        IDictionary<string, IReadOnlyList<CheckResultDetailReport>>? checkupDetails)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message;
        IReadOnlyList<CheckResultDetailReport>? details = null;

        if (checkupDetails is not null
            && checkupDetails.TryGetValue(result.Checkup.Id, out var captured)
            && captured.Count > 0)
        {
            details = [.. captured];
        }

        var suggestion = result.Suggestion is null
            ? null
            : new SuggestionReport(
                result.Suggestion.Name,
                string.IsNullOrWhiteSpace(result.Suggestion.Description)
                    ? null
                    : result.Suggestion.Description,
                result.Suggestion.HasSolution);

        return new CheckResultReport(
            result.Checkup.Id,
            result.Checkup.Title,
            result.Status,
            message,
            details,
            suggestion);
    }

    private static IReadOnlyList<SkippedCheckReport> CreateSkippedCheckReports(IEnumerable<SkipInfo> skippedCheckups)
    {
        return [.. skippedCheckups
            .GroupBy(s => s.CheckupId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var skip = g.First();
                return new SkippedCheckReport(skip.CheckupId, skip.skipReason, skip.isError);
            })
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)];
    }

    private static IReadOnlyList<UnresolvedCheckReport> CreateUnresolvedCheckReports(
        IDictionary<string, DiagnosticResult> results,
        IEnumerable<string> skippedFixes,
        IDictionary<string, string>? skippedFixReasons)
    {
        var skippedFixIds = skippedFixes as IReadOnlyCollection<string> ?? skippedFixes.ToArray();

        return [.. results.Values
            .Where(r => r.Status is Status.Error or Status.Warning)
            .Select(r =>
            {
                var hasAutoFix = r.Suggestion?.HasSolution ?? false;
                var wasSkipped = hasAutoFix
                    && skippedFixIds.Contains(r.Checkup.Id, StringComparer.OrdinalIgnoreCase);

                var fixStatus = FixStatus.Attempted;
                var reason = string.IsNullOrWhiteSpace(r.Message)
                    ? "The check still reports issues after applying fixes."
                    : r.Message!;

                if (!hasAutoFix)
                {
                    fixStatus = FixStatus.NotAvailable;
                    reason = r.Suggestion is { Description: { Length: > 0 } desc }
                        ? desc
                        : "No automatic fix is available for this checkup.";
                }
                else if (wasSkipped)
                {
                    fixStatus = FixStatus.Skipped;
                    if (skippedFixReasons is not null
                        && skippedFixReasons.TryGetValue(r.Checkup.Id, out var skipReason)
                        && !string.IsNullOrWhiteSpace(skipReason))
                    {
                        reason = skipReason;
                    }
                    else
                    {
                        reason = "Automatic fix was available but not attempted.";
                    }
                }

                return new UnresolvedCheckReport(
                    r.Checkup.Id,
                    r.Checkup.Title,
                    r.Status,
                    string.IsNullOrWhiteSpace(r.Message) ? null : r.Message,
                    fixStatus,
                    reason);
            })
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)];
    }
}
