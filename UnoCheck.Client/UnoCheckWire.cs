#nullable enable

using System.Collections.Generic;
using System.Text.Json.Serialization;

// Wire DTOs are a one-to-one mirror of the JSON: each property is already named by its
// [JsonPropertyName], so a per-property doc comment would only restate it. The types
// themselves are documented; the fields are documented by the contract (spec 003).
#pragma warning disable CS1591

namespace Uno.Check.Client;

/// <summary>
/// Wire shape of one uno-check JSONL line (contract: spec 003, schema 1.0). One envelope
/// covers the union of event fields — the <c>type</c> discriminator decides which of them
/// are meaningful, which is what lets a host parse the stream with a single type.
///
/// These mirror the CLI's writer types in <c>UnoCheck/Json/JsonOutput.cs</c>. The CLI cannot
/// reference this package (it still targets up to net6.0), so the two are pinned by a
/// round-trip test in <c>UnoCheck.Tests</c> rather than by sharing a definition.
/// </summary>
public sealed class UnoCheckEventLine
{
	[JsonPropertyName("type")] public string? Type { get; set; }
	[JsonPropertyName("id")] public string? Id { get; set; }
	[JsonPropertyName("name")] public string? Name { get; set; }
	[JsonPropertyName("message")] public string? Message { get; set; }
	[JsonPropertyName("checkup_count")] public int CheckupCount { get; set; }
	[JsonPropertyName("check")] public UnoCheckHealthCheckLine? Check { get; set; }
	[JsonPropertyName("solution")] public string? Solution { get; set; }
	[JsonPropertyName("success")] public bool Success { get; set; }
	[JsonPropertyName("error")] public string? Error { get; set; }
	[JsonPropertyName("report")] public UnoCheckReportLine? Report { get; set; }
	[JsonPropertyName("checkups")] public List<UnoCheckCatalogItemLine>? Checkups { get; set; }
}

public sealed class UnoCheckHealthCheckLine
{
	[JsonPropertyName("id")] public string? Id { get; set; }
	[JsonPropertyName("name")] public string? Name { get; set; }
	[JsonPropertyName("status")] public string? Status { get; set; }
	[JsonPropertyName("message")] public string? Message { get; set; }
	[JsonPropertyName("skip_reason")] public string? SkipReason { get; set; }
	[JsonPropertyName("fix")] public UnoCheckFixInfoLine? Fix { get; set; }
}

public sealed class UnoCheckFixInfoLine
{
	[JsonPropertyName("auto_fixable")] public bool AutoFixable { get; set; }

	/// <summary>
	/// Whether running the fix needs admin/root. Nullable on purpose: an engine predating the
	/// field sends nothing, and a host must be able to tell "not required" from "not reported"
	/// — see <see cref="UnoCheckCheckResult.RequiresElevation"/>.
	/// </summary>
	[JsonPropertyName("requires_elevation")] public bool? RequiresElevation { get; set; }
}

public sealed class UnoCheckReportLine
{
	[JsonPropertyName("status")] public string? Status { get; set; }
	[JsonPropertyName("reason")] public string? Reason { get; set; }
	[JsonPropertyName("summary")] public UnoCheckSummaryLine? Summary { get; set; }
}

public sealed class UnoCheckSummaryLine
{
	[JsonPropertyName("total")] public int Total { get; set; }
	[JsonPropertyName("ok")] public int Ok { get; set; }
	[JsonPropertyName("warning")] public int Warning { get; set; }
	[JsonPropertyName("error")] public int Error { get; set; }
	[JsonPropertyName("skipped")] public int Skipped { get; set; }
}

/// <summary>Catalog entry: schema 1.0 carries the human title in <c>name</c> ("title" in earlier drafts).</summary>
public sealed class UnoCheckCatalogItemLine
{
	[JsonPropertyName("id")] public string? Id { get; set; }
	[JsonPropertyName("name")] public string? Name { get; set; }
	[JsonPropertyName("title")] public string? Title { get; set; }
}

#if NET8_0_OR_GREATER
/// <summary>
/// Source-generated deserialization, so a trimmed or AOT host does not fall back to
/// reflection. netstandard2.0 consumers use the reflection path instead.
/// </summary>
[JsonSerializable(typeof(UnoCheckEventLine))]
public sealed partial class UnoCheckJsonContext : JsonSerializerContext;
#endif

#pragma warning restore CS1591
