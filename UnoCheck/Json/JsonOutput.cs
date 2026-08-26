#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetCheck.Models;

namespace DotNetCheck.Json
{
	/// <summary>
	/// Structured output for GUI/CI/agent consumers: JSONL progress events on stdout
	/// (and/or appended to a file for elevated child processes whose stdout cannot be
	/// redirected across the elevation boundary), ending with a final report event.
	/// Schema modeled on the maui CLI doctor's DoctorReport/FixInfo.
	/// </summary>
	public static class JsonlOutput
	{
		static readonly object _gate = new();
		static readonly JsonSerializerOptions _options = new()
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		static bool _toStdout;
		static string? _filePath;

		public static bool Enabled { get; private set; }

		public static string CorrelationId { get; } = Guid.NewGuid().ToString("n");

		public static void Init(bool toStdout, string? filePath)
		{
			_toStdout = toStdout;
			_filePath = filePath;
			Enabled = toStdout || !string.IsNullOrEmpty(filePath);
		}

		public static void Emit(JsonlEvent evt)
		{
			if (!Enabled)
				return;

			evt.CorrelationId = CorrelationId;
			evt.Timestamp = DateTimeOffset.UtcNow;

			var line = JsonSerializer.Serialize(evt, evt.GetType(), _options);

			lock (_gate)
			{
				if (_toStdout)
					Console.Out.WriteLine(line);

				if (!string.IsNullOrEmpty(_filePath))
					File.AppendAllText(_filePath, line + Environment.NewLine);
			}
		}

		public static string StatusName(Status status)
			=> status switch
			{
				Status.Ok => "ok",
				Status.Warning => "warning",
				_ => "error",
			};
	}

	public abstract class JsonlEvent
	{
		[JsonPropertyName("correlation_id")]
		public string? CorrelationId { get; set; }

		[JsonPropertyName("timestamp")]
		public DateTimeOffset Timestamp { get; set; }
	}

	public sealed class RunStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "run_started";

		[JsonPropertyName("schema_version")]
		public string SchemaVersion { get; set; } = "1.0";

		[JsonPropertyName("tool_version")]
		public string? ToolVersion { get; set; }

		[JsonPropertyName("channel")]
		public string? Channel { get; set; }

		[JsonPropertyName("targets")]
		public string[]? Targets { get; set; }

		[JsonPropertyName("checkup_count")]
		public int CheckupCount { get; set; }
	}

	public sealed class CheckupStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_started";

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("name")]
		public string? Name { get; set; }
	}

	public sealed class CheckupProgressEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_progress";

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }

		[JsonPropertyName("status")]
		public string? Status { get; set; }

		[JsonPropertyName("progress")]
		public int? Progress { get; set; }
	}

	public sealed class CheckupResultEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_result";

		[JsonPropertyName("check")]
		public HealthCheck? Check { get; set; }
	}

	public sealed class FixStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_started";

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("solution")]
		public string? Solution { get; set; }
	}

	public sealed class FixProgressEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_progress";

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }
	}

	public sealed class FixResultEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_result";

		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("success")]
		public bool Success { get; set; }

		[JsonPropertyName("error")]
		public string? Error { get; set; }
	}

	public sealed class ReportEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "report";

		[JsonPropertyName("report")]
		public DoctorReport? Report { get; set; }
	}

	public sealed class DoctorReport
	{
		[JsonPropertyName("schema_version")]
		public string SchemaVersion { get; set; } = "1.0";

		[JsonPropertyName("correlation_id")]
		public string? CorrelationId { get; set; }

		[JsonPropertyName("timestamp")]
		public DateTimeOffset Timestamp { get; set; }

		[JsonPropertyName("tool_version")]
		public string? ToolVersion { get; set; }

		[JsonPropertyName("status")]
		public string? Status { get; set; }

		[JsonPropertyName("checks")]
		public List<HealthCheck> Checks { get; set; } = new();

		[JsonPropertyName("summary")]
		public DoctorSummary Summary { get; set; } = new();
	}

	public sealed class HealthCheck
	{
		[JsonPropertyName("id")]
		public string? Id { get; set; }

		[JsonPropertyName("name")]
		public string? Name { get; set; }

		[JsonPropertyName("status")]
		public string? Status { get; set; }

		[JsonPropertyName("message")]
		public string? Message { get; set; }

		[JsonPropertyName("skip_reason")]
		public string? SkipReason { get; set; }

		[JsonPropertyName("fix")]
		public FixInfo? Fix { get; set; }
	}

	public sealed class FixInfo
	{
		[JsonPropertyName("issue_id")]
		public string? IssueId { get; set; }

		[JsonPropertyName("description")]
		public string? Description { get; set; }

		[JsonPropertyName("auto_fixable")]
		public bool AutoFixable { get; set; }

		[JsonPropertyName("command")]
		public string? Command { get; set; }
	}

	public sealed class DoctorSummary
	{
		[JsonPropertyName("total")]
		public int Total { get; set; }

		[JsonPropertyName("ok")]
		public int Ok { get; set; }

		[JsonPropertyName("warning")]
		public int Warning { get; set; }

		[JsonPropertyName("error")]
		public int Error { get; set; }

		[JsonPropertyName("skipped")]
		public int Skipped { get; set; }
	}
}
