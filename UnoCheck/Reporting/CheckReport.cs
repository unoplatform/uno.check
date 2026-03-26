#nullable enable

using System;
using System.Collections.Generic;
using DotNetCheck.Models;

namespace DotNetCheck.Reporting;

internal sealed record CheckReport(
    string ToolVersion,
    string ManifestVersion,
    string ManifestChannel,
    DateTimeOffset StartedAtUtc,
    DateTimeOffset CompletedAtUtc,
    double DurationSeconds,
    int ExitCode,
    Platform Platform,
    IReadOnlyList<string> Frameworks,
    IReadOnlyList<string> TargetPlatforms,
    IReadOnlyList<CheckResultReport> Results,
    IReadOnlyList<SkippedCheckReport> SkippedCheckups,
    IReadOnlyList<string> SkippedFixes,
    IReadOnlyList<UnresolvedCheckReport> UnresolvedCheckups,
    bool HasErrors,
    bool HasWarnings);

internal sealed record CheckResultReport(
    string Id,
    string Title,
    Status Status,
    string? Message,
    IReadOnlyList<CheckResultDetailReport>? Details,
    SuggestionReport? Suggestion);

internal sealed record CheckResultDetailReport(string Message, Status? Status);

internal sealed record UnresolvedCheckReport(
    string Id,
    string Title,
    Status Status,
    string? Message,
    FixStatus FixStatus,
    string Reason);

internal enum FixStatus
{
    NotAvailable,
    Skipped,
    Attempted
}

internal sealed record SuggestionReport(string Name, string? Description, bool HasSolution);

internal sealed record SkippedCheckReport(string Id, string Reason, bool IsError);
