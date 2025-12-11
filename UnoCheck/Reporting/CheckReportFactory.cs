#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using DotNetCheck;
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
        int exitCode)
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

        return new CheckReport
        {
			ToolVersion = ToolInfo.CurrentVersion.ToNormalizedString(),
			ManifestVersion = manifest?.Check?.ToolVersion ?? string.Empty,
			ManifestChannel = manifestChannel.ToString().ToLowerInvariant(),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            DurationSeconds = duration.TotalSeconds,
            ExitCode = exitCode,
            Platform = platform,
            Frameworks = settings.Frameworks ?? Array.Empty<string>(),
            TargetPlatforms = settings.TargetPlatforms ?? Array.Empty<string>(),
            Results = CreateCheckResultReports(results),
            SkippedCheckups = CreateSkippedCheckReports(skippedCheckups),
            SkippedFixes = skippedFixes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            HasErrors = results.Values.Any(r => r.Status == Status.Error),
            HasWarnings = results.Values.Any(r => r.Status == Status.Warning)
        };
    }

    private static IReadOnlyList<CheckResultReport> CreateCheckResultReports(
        IDictionary<string, DiagnosticResult> results)
    {
        var reports = results.Values
            .Select(CreateCheckResultReport)
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return reports;
    }

    private static CheckResultReport CreateCheckResultReport(DiagnosticResult result)
    {
        var message = string.IsNullOrWhiteSpace(result.Message) ? null : result.Message;

        return new CheckResultReport
        {
            Id = result.Checkup.Id,
            Title = result.Checkup.Title,
            Status = result.Status,
            Message = message,
            Suggestion = result.Suggestion is null
                ? null
                : new SuggestionReport
                {
                    Name = result.Suggestion.Name,
                    Description = string.IsNullOrWhiteSpace(result.Suggestion.Description)
                        ? null
                        : result.Suggestion.Description,
                    HasSolution = result.Suggestion.HasSolution
                }
        };
    }

    private static IReadOnlyList<SkippedCheckReport> CreateSkippedCheckReports(IEnumerable<SkipInfo> skippedCheckups)
    {
        return skippedCheckups
            .GroupBy(s => s.CheckupId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var skip = g.First();
                return new SkippedCheckReport
                {
                    Id = skip.CheckupId,
                    Reason = skip.skipReason,
                    IsError = skip.isError
                };
            })
            .OrderBy(s => s.Id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
