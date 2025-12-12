#nullable enable

using System;
using System.Collections.Generic;
using DotNetCheck.Models;

namespace DotNetCheck.Reporting;

internal sealed record CheckReport
{
    public string ToolVersion { get; init; } = string.Empty;

    public string ManifestVersion { get; init; } = string.Empty;

    public string ManifestChannel { get; init; } = string.Empty;

    public DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }

    public double DurationSeconds { get; init; }

    public int ExitCode { get; init; }

    public Platform Platform { get; init; }

    public IReadOnlyList<string> Frameworks { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> TargetPlatforms { get; init; } = Array.Empty<string>();

    public IReadOnlyList<CheckResultReport> Results { get; init; } = Array.Empty<CheckResultReport>();

    public IReadOnlyList<SkippedCheckReport> SkippedCheckups { get; init; } = Array.Empty<SkippedCheckReport>();

    public IReadOnlyList<string> SkippedFixes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<UnresolvedCheckReport> UnresolvedCheckups { get; init; } = Array.Empty<UnresolvedCheckReport>();

    public bool HasErrors { get; init; }

    public bool HasWarnings { get; init; }
}

internal sealed record CheckResultReport
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public Status Status { get; init; }

    public string? Message { get; init; }

    public IReadOnlyList<CheckResultDetailReport>? Details { get; init; }

    public SuggestionReport? Suggestion { get; init; }
}

internal sealed record CheckResultDetailReport
{
    public string Message { get; init; } = string.Empty;

    public Status? Status { get; init; }
}

internal sealed record UnresolvedCheckReport
{
    public string Id { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;

    public Status Status { get; init; }

    public string? Message { get; init; }

    public FixStatus FixStatus { get; init; }

    public string Reason { get; init; } = string.Empty;
}

internal enum FixStatus
{
    NotAvailable,
    Skipped,
    Attempted
}

internal sealed record SuggestionReport
{
    public string Name { get; init; } = string.Empty;

    public string? Description { get; init; }

    public bool HasSolution { get; init; }
}

internal sealed record SkippedCheckReport
{
    public string Id { get; init; } = string.Empty;

    public string Reason { get; init; } = string.Empty;

    public bool IsError { get; init; }
}
