using System.Text.Json;
using DotNetCheck.Json;
using DotNetCheck.Models;

namespace UnoCheck.Tests;

/// <summary>
/// Snapshot-style tests pinning the JSONL wire contract: event type discriminators,
/// snake_case field names, null omission, and status-string vocabularies. A rename
/// that compiles must fail here before it silently breaks external consumers.
/// </summary>
public class JsonlEventSerializationTests
{
    static JsonElement Roundtrip(JsonlEvent evt)
        => JsonDocument.Parse(JsonlOutput.Render(evt)).RootElement;

    [Fact]
    public void Every_Event_Carries_Type_CorrelationId_And_Timestamp()
    {
        JsonlEvent[] events =
        [
            new RunStartedEvent(),
            new CheckupStartedEvent(),
            new CheckupProgressEvent(),
            new CheckupResultEvent(),
            new FixStartedEvent(),
            new FixProgressEvent(),
            new FixResultEvent(),
            new ReportEvent(),
        ];

        foreach (var json in events.Select(Roundtrip))
        {
            Assert.False(string.IsNullOrEmpty(json.GetProperty("type").GetString()));
            Assert.False(string.IsNullOrEmpty(json.GetProperty("correlation_id").GetString()));
            Assert.True(json.TryGetProperty("timestamp", out _));
        }
    }

    [Theory]
    [InlineData(typeof(RunStartedEvent), "run_started")]
    [InlineData(typeof(CheckupStartedEvent), "checkup_started")]
    [InlineData(typeof(CheckupProgressEvent), "checkup_progress")]
    [InlineData(typeof(CheckupResultEvent), "checkup_result")]
    [InlineData(typeof(FixStartedEvent), "fix_started")]
    [InlineData(typeof(FixProgressEvent), "fix_progress")]
    [InlineData(typeof(FixResultEvent), "fix_result")]
    [InlineData(typeof(ReportEvent), "report")]
    public void Type_Discriminators_Are_Stable(Type eventType, string expected)
    {
        var evt = (JsonlEvent)Activator.CreateInstance(eventType)!;
        Assert.Equal(expected, Roundtrip(evt).GetProperty("type").GetString());
    }

    [Fact]
    public void RunStarted_Field_Names_Are_Stable()
    {
        var json = Roundtrip(new RunStartedEvent
        {
            ToolVersion = "1.2.3",
            Channel = "Default",
            Targets = ["wasm", "android"],
            CheckupCount = 7,
        });

        Assert.Equal("1.0", json.GetProperty("schema_version").GetString());
        Assert.Equal("1.2.3", json.GetProperty("tool_version").GetString());
        Assert.Equal("Default", json.GetProperty("channel").GetString());
        Assert.Equal(2, json.GetProperty("targets").GetArrayLength());
        Assert.Equal(7, json.GetProperty("checkup_count").GetInt32());
    }

    [Fact]
    public void CheckupResult_Serializes_Full_HealthCheck_Shape()
    {
        var json = Roundtrip(new CheckupResultEvent
        {
            Check = new HealthCheck
            {
                Id = "dotnet",
                Name = ".NET SDK",
                Status = "error",
                Message = "not installed",
                Fix = new FixInfo
                {
                    IssueId = "dotnet",
                    Description = "Install it",
                    AutoFixable = true,
                    Args = ["--fix", "--only", "dotnet", "--non-interactive"],
                },
            },
        });

        var check = json.GetProperty("check");
        Assert.Equal("dotnet", check.GetProperty("id").GetString());
        Assert.Equal(".NET SDK", check.GetProperty("name").GetString());
        Assert.Equal("error", check.GetProperty("status").GetString());
        Assert.Equal("not installed", check.GetProperty("message").GetString());

        var fix = check.GetProperty("fix");
        Assert.Equal("dotnet", fix.GetProperty("issue_id").GetString());
        Assert.Equal("Install it", fix.GetProperty("description").GetString());
        Assert.True(fix.GetProperty("auto_fixable").GetBoolean());
        Assert.Equal(4, fix.GetProperty("args").GetArrayLength());
        Assert.Equal("--fix", fix.GetProperty("args")[0].GetString());
        Assert.Equal("dotnet", fix.GetProperty("args")[2].GetString());
    }

    [Fact]
    public void Null_Fields_Are_Omitted_Not_Emitted_As_Null()
    {
        var json = Roundtrip(new CheckupResultEvent
        {
            Check = new HealthCheck { Id = "ok-check", Name = "Ok", Status = "ok" },
        });

        var check = json.GetProperty("check");
        Assert.False(check.TryGetProperty("message", out _));
        Assert.False(check.TryGetProperty("skip_reason", out _));
        Assert.False(check.TryGetProperty("fix", out _));
    }

    [Fact]
    public void Skipped_Check_Carries_Skip_Reason()
    {
        var json = Roundtrip(new CheckupResultEvent
        {
            Check = new HealthCheck { Id = "git", Name = "Git", Status = "skipped", SkipReason = "Not required" },
        });

        Assert.Equal("skipped", json.GetProperty("check").GetProperty("status").GetString());
        Assert.Equal("Not required", json.GetProperty("check").GetProperty("skip_reason").GetString());
    }

    [Fact]
    public void FixResult_Field_Names_Are_Stable()
    {
        var ok = Roundtrip(new FixResultEvent { Id = "dotnet", Success = true });
        Assert.True(ok.GetProperty("success").GetBoolean());
        Assert.False(ok.TryGetProperty("error", out _));

        var failed = Roundtrip(new FixResultEvent { Id = "dotnet", Success = false, Error = "boom" });
        Assert.False(failed.GetProperty("success").GetBoolean());
        Assert.Equal("boom", failed.GetProperty("error").GetString());
    }

    [Fact]
    public void Report_Serializes_Summary_And_Status_Vocabulary()
    {
        var json = Roundtrip(new ReportEvent
        {
            Report = new Report
            {
                CorrelationId = "abc",
                ToolVersion = "1.2.3",
                Status = "degraded",
                Checks = [new HealthCheck { Id = "a", Name = "A", Status = "warning" }],
                Summary = new Summary { Total = 1, Ok = 0, Warning = 1, Error = 0, Skipped = 0 },
            },
        });

        var report = json.GetProperty("report");
        Assert.Equal("1.0", report.GetProperty("schema_version").GetString());
        Assert.Equal("degraded", report.GetProperty("status").GetString());
        Assert.Equal(1, report.GetProperty("checks").GetArrayLength());

        var summary = report.GetProperty("summary");
        foreach (var field in new[] { "total", "ok", "warning", "error", "skipped" })
            Assert.True(summary.TryGetProperty(field, out _), $"summary.{field} missing");
    }

    [Fact]
    public void CheckupCatalog_Field_Names_Are_Stable()
    {
        var json = Roundtrip(new CheckupCatalogEvent
        {
            Checkups = [new CheckupCatalogItem { Id = "openjdk", Name = "OpenJdkCheckup", Title = "OpenJDK 17" }],
        });

        Assert.Equal("checkup_catalog", json.GetProperty("type").GetString());
        Assert.Equal("1.0", json.GetProperty("schema_version").GetString());

        var item = json.GetProperty("checkups")[0];
        Assert.Equal("openjdk", item.GetProperty("id").GetString());
        Assert.Equal("OpenJdkCheckup", item.GetProperty("name").GetString());
        Assert.Equal("OpenJDK 17", item.GetProperty("title").GetString());
    }

    [Theory]
    [InlineData(Status.Ok, "ok")]
    [InlineData(Status.Warning, "warning")]
    [InlineData(Status.Error, "error")]
    public void StatusName_Maps_Engine_Status_To_Wire_Vocabulary(Status status, string expected)
        => Assert.Equal(expected, JsonlOutput.StatusName(status));

    [Fact]
    public void Rendered_Line_Is_Single_Line_Jsonl()
    {
        var line = JsonlOutput.Render(new CheckupProgressEvent { Id = "x", Message = "multi\nline\nmessage" });
        Assert.DoesNotContain('\n', line);
        Assert.DoesNotContain('\r', line);
        Assert.Equal("multi\nline\nmessage",
            JsonDocument.Parse(line).RootElement.GetProperty("message").GetString());
    }
}
