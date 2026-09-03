#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DotNetCheck.Models;

namespace DotNetCheck.Json
{
	/// <summary>
	/// Structured output for GUI/CI/agent consumers: JSONL progress events on stdout
	/// (and/or appended to a file for elevated child processes whose stdout cannot be
	/// redirected across the elevation boundary), ending with a final report event.
	/// The event schema is documented in specs/003-structured-json-output.
	/// Wire types are internal on purpose: the contract is the JSON, not the assembly surface.
	/// </summary>
	internal static class JsonlOutput
	{
		internal const string StatusOk = "ok";
		internal const string StatusWarning = "warning";
		internal const string StatusError = "error";
		internal const string StatusSkipped = "skipped";

		static readonly object _gate = new();
		static readonly JsonSerializerOptions _options = new()
		{
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		};

		static TextWriter? _stdout;
		static StreamWriter? _fileWriter;

		public static bool Enabled { get; private set; }

		/// <summary>
		/// Stable per run. Regenerated on each Init unless the caller supplies one —
		/// a host launching an elevated fix child passes its own id through
		/// --correlation-id so both processes report as one logical run.
		/// </summary>
		public static string CorrelationId { get; private set; } = Guid.NewGuid().ToString("n");

		/// <summary>
		/// Enables structured output. <paramref name="stdout"/> is the writer that owns the
		/// event stream — callers should capture the process stdout and then redirect
		/// Console.Out to stderr so no stray write can corrupt the stream.
		///
		/// The file sink only ever writes a file it created itself: the path must not
		/// already exist, so no stale lines from a previous run and no writing through a
		/// symlink planted at the path by another local process (this process may be
		/// running elevated). Junctions or links in parent directories are not detected,
		/// so callers should use a directory private to the launching user. A path that
		/// cannot be created disables the sink with a warning on stderr rather than
		/// failing the run later, mid-stream.
		/// </summary>
		public static void Init(TextWriter? stdout, string? filePath, string? correlationId = null)
		{
			_stdout = stdout;
			_fileWriter?.Dispose();
			_fileWriter = null;
			CorrelationId = string.IsNullOrWhiteSpace(correlationId)
				? Guid.NewGuid().ToString("n")
				: correlationId;

			if (!string.IsNullOrEmpty(filePath))
			{
				try
				{
					var fullPath = Path.GetFullPath(filePath);
					var dir = Path.GetDirectoryName(fullPath);
					if (!string.IsNullOrEmpty(dir))
						Directory.CreateDirectory(dir);

					// CreateNew: refuse to write through anything that already exists at the
					// path — a leftover file from a previous run or a link planted by another
					// process. FileShare.ReadWrite lets the host tail while we append.
					var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite);
					_fileWriter = new StreamWriter(stream) { AutoFlush = true };
				}
				catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
					or ArgumentException or NotSupportedException or System.Security.SecurityException)
				{
					Console.Error.WriteLine($"warning: --json-file requires a new, creatable path; file sink disabled: {ex.Message}");
					_fileWriter = null;
				}
			}

			Enabled = _stdout is not null || _fileWriter is not null;
		}

		public static void Emit(JsonlEvent evt)
		{
			if (!Enabled)
				return;

			var line = Render(evt);

			lock (_gate)
			{
				if (_stdout is not null)
				{
					try
					{
						// Flush per line so piped consumers see events as they happen.
						_stdout.WriteLine(line);
						_stdout.Flush();
					}
					catch (Exception ex) when (ex is IOException or ObjectDisposedException)
					{
						// A broken/closed pipe (consumer stopped reading) must not abort
						// the run it reports on — same rule as the file sink.
						Console.Error.WriteLine($"warning: stdout event stream write failed, sink disabled: {ex.Message}");
						_stdout = null;
						Enabled = _fileWriter is not null;
					}
				}

				if (_fileWriter is not null)
				{
					try
					{
						_fileWriter.WriteLine(line);
					}
					catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
					{
						// A machine-readable channel must never abort the run it reports on.
						Console.Error.WriteLine($"warning: --json-file write failed, file sink disabled: {ex.Message}");
						_fileWriter = null;
						Enabled = _stdout is not null;
					}
				}
			}
		}

		/// <summary>Serializes the event to a single JSONL line. Events are stamped at construction.</summary>
		internal static string Render(JsonlEvent evt)
			=> JsonSerializer.Serialize(evt, evt.GetType(), _options);

		/// <summary>
		/// Raw-args detection of structured mode, used before command-line parsing runs so
		/// the console window can be handled first. Matches exactly --json, --json-file, and
		/// --json-file=&lt;path&gt; — never other flags that merely share the prefix.
		/// </summary>
		public static bool IsStructuredOutputRequested(string[]? args)
			=> args?.Any(a =>
				a.Equals("--json", StringComparison.OrdinalIgnoreCase)
				|| a.Equals("--json-file", StringComparison.OrdinalIgnoreCase)
				|| a.StartsWith("--json-file=", StringComparison.OrdinalIgnoreCase)) ?? false;

		public static string StatusName(Status status)
			=> status switch
			{
				Status.Ok => StatusOk,
				Status.Warning => StatusWarning,
				_ => StatusError,
			};

		/// <summary>
		/// Assembles the final report. Extracted so summary counting — the most
		/// host-visible mapping in the contract — is unit-testable.
		/// </summary>
		internal static Report BuildReport(string? toolVersion, string overallStatus, IReadOnlyCollection<HealthCheck> checks, string? reason = null)
			=> new()
			{
				CorrelationId = CorrelationId,
				Timestamp = DateTimeOffset.UtcNow,
				ToolVersion = toolVersion,
				Status = overallStatus,
				Reason = reason,
				Checks = checks.ToList(),
				Summary = new Summary
				{
					Total = checks.Count,
					Ok = checks.Count(c => c.Status == StatusOk),
					Warning = checks.Count(c => c.Status == StatusWarning),
					Error = checks.Count(c => c.Status == StatusError),
					Skipped = checks.Count(c => c.Status == StatusSkipped),
				},
			};

		static readonly Regex _safeIdRegex = new("^[A-Za-z0-9._-]+$", RegexOptions.Compiled);

		/// <summary>
		/// Checkup ids flow into host-executed fix invocations; only vouch for ids that
		/// cannot smuggle argument or shell metacharacters (workload checkup ids embed a
		/// version string sourced from the manifest / environment).
		/// </summary>
		internal static bool IsSafeCheckupId(string? id)
			=> !string.IsNullOrEmpty(id) && _safeIdRegex.IsMatch(id);
	}

	internal abstract class JsonlEvent
	{
		protected JsonlEvent()
		{
			// Stamped at construction: Render never mutates the event it is handed.
			CorrelationId = JsonlOutput.CorrelationId;
			Timestamp = DateTimeOffset.UtcNow;
		}

		[JsonPropertyName("correlation_id")]
		public string CorrelationId { get; }

		[JsonPropertyName("timestamp")]
		public DateTimeOffset Timestamp { get; }
	}

	internal sealed class RunStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "run_started";

		[JsonPropertyName("schema_version")]
		public string SchemaVersion => "1.0";

		[JsonPropertyName("tool_version")]
		public string? ToolVersion { get; init; }

		[JsonPropertyName("channel")]
		public string? Channel { get; init; }

		[JsonPropertyName("targets")]
		public string[]? Targets { get; init; }

		[JsonPropertyName("checkup_count")]
		public int CheckupCount { get; init; }
	}

	internal sealed class CheckupStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_started";

		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("name")]
		public string? Name { get; init; }
	}

	internal sealed class CheckupProgressEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_progress";

		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("message")]
		public string? Message { get; init; }

		[JsonPropertyName("status")]
		public string? Status { get; init; }

		[JsonPropertyName("progress")]
		public int? Progress { get; init; }
	}

	internal sealed class CheckupResultEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_result";

		[JsonPropertyName("check")]
		public HealthCheck? Check { get; init; }
	}

	internal sealed class FixStartedEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_started";

		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("solution")]
		public string? Solution { get; init; }
	}

	internal sealed class FixProgressEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_progress";

		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("message")]
		public string? Message { get; init; }
	}

	internal sealed class FixResultEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "fix_result";

		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("success")]
		public bool Success { get; init; }

		[JsonPropertyName("error")]
		public string? Error { get; init; }
	}

	internal sealed class ReportEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "report";

		[JsonPropertyName("report")]
		public Report? Report { get; init; }
	}

	/// <summary>
	/// Emitted by `list --json`: the catalog of checkups applicable to this machine and
	/// target selection, for hosts building checkup-selection UIs (feeding --only/--skip).
	/// </summary>
	internal sealed class CheckupCatalogEvent : JsonlEvent
	{
		[JsonPropertyName("type")]
		public string Type => "checkup_catalog";

		[JsonPropertyName("schema_version")]
		public string SchemaVersion => "1.0";

		[JsonPropertyName("checkups")]
		public IReadOnlyList<CheckupCatalogItem> Checkups { get; init; } = Array.Empty<CheckupCatalogItem>();
	}

	internal sealed class CheckupCatalogItem
	{
		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("name")]
		public string? Name { get; init; }

		[JsonPropertyName("type_name")]
		public string? TypeName { get; init; }
	}

	internal sealed class Report
	{
		[JsonPropertyName("schema_version")]
		public string SchemaVersion => "1.0";

		[JsonPropertyName("correlation_id")]
		public string? CorrelationId { get; init; }

		[JsonPropertyName("timestamp")]
		public DateTimeOffset Timestamp { get; init; }

		[JsonPropertyName("tool_version")]
		public string? ToolVersion { get; init; }

		[JsonPropertyName("status")]
		public string? Status { get; init; }

		[JsonPropertyName("reason")]
		public string? Reason { get; init; }

		[JsonPropertyName("checks")]
		public List<HealthCheck> Checks { get; init; } = new();

		[JsonPropertyName("summary")]
		public Summary Summary { get; init; } = new();
	}

	internal sealed class HealthCheck
	{
		[JsonPropertyName("id")]
		public string? Id { get; init; }

		[JsonPropertyName("name")]
		public string? Name { get; init; }

		[JsonPropertyName("status")]
		public string? Status { get; init; }

		[JsonPropertyName("message")]
		public string? Message { get; init; }

		[JsonPropertyName("skip_reason")]
		public string? SkipReason { get; init; }

		[JsonPropertyName("fix")]
		public FixInfo? Fix { get; init; }
	}

	internal sealed class FixInfo
	{
		[JsonPropertyName("issue_id")]
		public string? IssueId { get; init; }

		[JsonPropertyName("description")]
		public string? Description { get; init; }

		[JsonPropertyName("auto_fixable")]
		public bool AutoFixable { get; init; }

		/// <summary>
		/// Argument vector for a per-item fix, to be passed straight to the uno-check
		/// process with no shell involved. Never a pre-joined command string: checkup
		/// ids can embed manifest-sourced version text, and a joined string invites
		/// hosts to run it through a shell — elevated.
		/// </summary>
		[JsonPropertyName("args")]
		public IReadOnlyList<string>? Args { get; init; }
	}

	internal sealed class Summary
	{
		[JsonPropertyName("total")]
		public int Total { get; init; }

		[JsonPropertyName("ok")]
		public int Ok { get; init; }

		[JsonPropertyName("warning")]
		public int Warning { get; init; }

		[JsonPropertyName("error")]
		public int Error { get; init; }

		[JsonPropertyName("skipped")]
		public int Skipped { get; init; }
	}
}
