using System.Text.Json;
using DotNetCheck;
using DotNetCheck.Cli;
using DotNetCheck.Json;
using DotNetCheck.Models;
using Spectre.Console.Cli;
using AnsiConsole = Spectre.Console.AnsiConsole;

namespace UnoCheck.Tests;

/// <summary>Fake checkup for exercising BuildHealthCheck and the --only filter.</summary>
file class FakeCheckup : Checkup
{
    readonly string _id;
    readonly CheckupDependency[] _dependencies;

    public FakeCheckup(string id, params CheckupDependency[] dependencies)
    {
        _id = id;
        _dependencies = dependencies;
    }

    public override string Id => _id;
    public override string Title => $"Fake {_id}";

    public override IEnumerable<CheckupDependency> DeclareDependencies(IEnumerable<string> checkupIds)
        => _dependencies;

    public override Task<DiagnosticResult> Examine(SharedState history)
        => Task.FromResult(DiagnosticResult.Ok(this));
}

file class NoopSolution : Solution
{
}

public class BuildHealthCheckTests
{
    [Fact]
    public void Ok_Result_Has_No_Fix_And_No_Message()
    {
        var checkup = new FakeCheckup("dotnet");
        var check = CheckCommand.BuildHealthCheck(checkup, DiagnosticResult.Ok(checkup));

        Assert.Equal("dotnet", check.Id);
        Assert.Equal("Fake dotnet", check.Name);
        Assert.Equal("ok", check.Status);
        Assert.Null(check.Message);
        Assert.Null(check.Fix);
    }

    [Fact]
    public void Error_With_Solution_Is_AutoFixable_With_Args_Vector()
    {
        var checkup = new FakeCheckup("androidsdk");
        var suggestion = new Suggestion("Install the SDK", new NoopSolution());
        var diagnosis = new DiagnosticResult(Status.Error, checkup, "SDK missing", suggestion);

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.Equal("error", check.Status);
        Assert.Equal("SDK missing", check.Message);
        Assert.NotNull(check.Fix);
        Assert.Equal("androidsdk", check.Fix.IssueId);
        Assert.True(check.Fix.AutoFixable);
        Assert.Equal(["--fix", "--only", "androidsdk", "--non-interactive"], check.Fix.Args);
    }

    [Fact]
    public void Unsafe_Checkup_Id_Never_Yields_Fix_Args()
    {
        // Ids flow into a host-executed, often elevated, fix invocation. An id carrying
        // argument/shell metacharacters (possible via manifest-sourced version strings)
        // must not be vouched for as auto-fixable.
        var checkup = new FakeCheckup("dotnetworkloads-1.0 --evil \"x\"");
        var diagnosis = new DiagnosticResult(Status.Error, checkup, new Suggestion("fix it", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.NotNull(check.Fix);
        Assert.False(check.Fix.AutoFixable);
        Assert.Null(check.Fix.Args);
    }

    [Fact]
    public void Error_With_Suggestion_But_No_Solution_Is_Not_AutoFixable()
    {
        var checkup = new FakeCheckup("xcode");
        var diagnosis = new DiagnosticResult(Status.Error, checkup, new Suggestion("Install Xcode manually"));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.NotNull(check.Fix);
        Assert.False(check.Fix.AutoFixable);
        Assert.Null(check.Fix.Args);
        Assert.Equal("Install Xcode manually", check.Fix.Description);
    }

    [Fact]
    public void Fix_Description_Combines_Name_And_Description_When_Both_Present()
    {
        var checkup = new FakeCheckup("hyperv");
        var diagnosis = new DiagnosticResult(
            Status.Warning, checkup,
            new Suggestion("Enable Hyper-V", "Required for hardware-accelerated emulators", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.Equal("warning", check.Status);
        Assert.Equal("Enable Hyper-V: Required for hardware-accelerated emulators", check.Fix!.Description);
    }

    [Fact]
    public void Ok_Result_With_Suggestion_Emits_No_Fix()
    {
        var checkup = new FakeCheckup("git");
        var diagnosis = new DiagnosticResult(Status.Ok, checkup, new Suggestion("Nothing to do", new NoopSolution()));

        Assert.Null(CheckCommand.BuildHealthCheck(checkup, diagnosis).Fix);
    }

    [Fact]
    public void Error_Without_Message_Falls_Back_To_Suggestion_Description()
    {
        // Some checkups (e.g. a workloads mismatch) report status without a message;
        // hosts would render a bare card with no explanation.
        var checkup = new FakeCheckup("dotnetworkloads-10.0.201");
        var diagnosis = new DiagnosticResult(
            Status.Error, checkup,
            new Suggestion("Install .NET workloads", "Installing .NET workloads.", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.Equal("Installing .NET workloads.", check.Message);
    }

    [Fact]
    public void Error_Without_Message_Or_Description_Falls_Back_To_Suggestion_Name()
    {
        var checkup = new FakeCheckup("unosdk");
        var diagnosis = new DiagnosticResult(Status.Error, checkup, new Suggestion("Install the SDK", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.Equal("Install the SDK", check.Message);
    }

    [Fact]
    public void Explicit_Message_Wins_Over_Suggestion_Fallback()
    {
        var checkup = new FakeCheckup("unosdk");
        var diagnosis = new DiagnosticResult(
            Status.Error, checkup, "Uno.Sdk 6.7.22 is not installed.",
            new Suggestion("Install the SDK", new NoopSolution()));

        var check = CheckCommand.BuildHealthCheck(checkup, diagnosis);

        Assert.Equal("Uno.Sdk 6.7.22 is not installed.", check.Message);
    }
}

public class IsCallerNamedForFixTests
{
    [Fact]
    public void Without_Only_Every_Checkup_Qualifies()
    {
        Assert.True(CheckCommand.IsCallerNamedForFix("dotnetworkloads-10.0.201", null));
        Assert.True(CheckCommand.IsCallerNamedForFix("dotnetworkloads-10.0.201", []));
    }

    [Fact]
    public void Caller_Named_Id_Qualifies_Case_Insensitively()
    {
        Assert.True(CheckCommand.IsCallerNamedForFix("unosdk", ["UnoSdk"]));
    }

    [Fact]
    public void Dependency_Included_Checkup_Is_Not_AutoFixed()
    {
        // --only unosdk pulls dotnet/workloads in as examine-only dependencies; fixing
        // them would raise an elevation prompt for a command the user never requested.
        Assert.False(CheckCommand.IsCallerNamedForFix("dotnetworkloads-10.0.201", ["unosdk"]));
        Assert.False(CheckCommand.IsCallerNamedForFix("dotnet", ["unosdk"]));
    }

    [Fact]
    public void Prefix_Does_Not_Qualify_A_Caller_Name()
    {
        // Caller ids match exactly — the one-way prefix rule is for dependency ids only.
        Assert.False(CheckCommand.IsCallerNamedForFix("dotnetworkloads-10.0.201", ["dotnetworkloads"]));
    }
}

public class SafeCheckupIdTests
{
    [Theory]
    [InlineData("openjdk", true)]
    [InlineData("dotnetworkloads-10.0.100", true)]
    [InlineData("https-dev-cert", true)]
    [InlineData("Vs_Win.Workloads", true)]
    [InlineData("id with spaces", false)]
    [InlineData("id\"quote", false)]
    [InlineData("id;semicolon", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_Argument_Safe_Ids_Are_Vouched_For(string? id, bool expected)
        => Assert.Equal(expected, JsonlOutput.IsSafeCheckupId(id));
}

public class ApplyOnlyFilterTests
{
    static List<Checkup> Graph(params Checkup[] checkups) => checkups.ToList();

    [Fact]
    public void No_Only_Argument_Returns_List_Unchanged()
    {
        var graph = Graph(new FakeCheckup("a"), new FakeCheckup("b"));

        Assert.Same(graph, CheckCommand.ApplyOnlyFilter(graph, null, out var unknown1));
        Assert.Empty(unknown1);
        Assert.Same(graph, CheckCommand.ApplyOnlyFilter(graph, [], out var unknown2));
        Assert.Empty(unknown2);
    }

    [Fact]
    public void Exact_Id_Selects_Only_That_Checkup()
    {
        var graph = Graph(new FakeCheckup("openjdk"), new FakeCheckup("git"), new FakeCheckup("hyperv"));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["openjdk"], out var unknown);

        Assert.Empty(unknown);
        Assert.Single(filtered);
        Assert.Equal("openjdk", filtered[0].Id);
    }

    [Fact]
    public void Matching_Is_Case_Insensitive()
    {
        var graph = Graph(new FakeCheckup("openjdk"));
        Assert.Single(CheckCommand.ApplyOnlyFilter(graph, ["OpenJDK"], out _));
    }

    [Fact]
    public void Caller_Ids_Do_Not_Prefix_Match_Siblings()
    {
        // --only dotnet must not sweep in every dotnet* sibling: a per-item Fix
        // invocation may never touch checkups the caller did not name.
        var graph = Graph(
            new FakeCheckup("dotnet"),
            new FakeCheckup("dotnetworkloads-10.0.100"),
            new FakeCheckup("dotnetnewunotemplates"));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["dotnet"], out var unknown);

        Assert.Empty(unknown);
        Assert.Single(filtered);
        Assert.Equal("dotnet", filtered[0].Id);
    }

    [Fact]
    public void Required_Dependencies_Are_Pulled_In_Transitively()
    {
        var graph = Graph(
            new FakeCheckup("dotnet"),
            new FakeCheckup("androidsdk", new CheckupDependency("openjdk")),
            new FakeCheckup("openjdk", new CheckupDependency("dotnet")),
            new FakeCheckup("git"));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["androidsdk"], out var unknown);

        Assert.Empty(unknown);
        Assert.Equal(["dotnet", "androidsdk", "openjdk"], filtered.Select(c => c.Id).ToArray());
    }

    [Fact]
    public void Dependency_Ids_Keep_The_One_Way_Prefix_Rule()
    {
        // Dependencies legitimately declare prefix ids ("dotnetworkloads" matching the
        // versioned "dotnetworkloads-<ver>" checkup) — that direction must keep working.
        var graph = Graph(
            new FakeCheckup("dotnetworkloads-10.0.100"),
            new FakeCheckup("vswin", new CheckupDependency("dotnetworkloads")));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["vswin"], out var unknown);

        Assert.Empty(unknown);
        Assert.Equal(2, filtered.Count);
    }

    [Fact]
    public void Optional_Dependencies_Are_Not_Pulled_In()
    {
        var graph = Graph(
            new FakeCheckup("androidemulator", new CheckupDependency("androidsdk") { IsRequired = false }),
            new FakeCheckup("androidsdk"));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["androidemulator"], out _);

        Assert.Single(filtered);
        Assert.Equal("androidemulator", filtered[0].Id);
    }

    [Fact]
    public void Unmatched_Ids_Are_Reported_Not_Swallowed()
    {
        // A typo'd --only must fail loudly instead of producing a passing empty run.
        var graph = Graph(new FakeCheckup("openjdk"));

        var filtered = CheckCommand.ApplyOnlyFilter(graph, ["opnejdk-typo", "openjdk"], out var unknown);

        Assert.Equal(["opnejdk-typo"], unknown);
        Assert.Single(filtered);
    }

    [Fact]
    public void Empty_Or_Whitespace_Ids_Are_Reported_As_Unknown()
    {
        var graph = Graph(new FakeCheckup("openjdk"));

        CheckCommand.ApplyOnlyFilter(graph, ["", "  "], out var unknown);

        Assert.Equal(2, unknown.Count);
        Assert.All(unknown, u => Assert.Equal("(empty)", u));
    }
}

public class StructuredModeDetectionTests
{
    [Theory]
    [InlineData(new[] { "--json" }, true)]
    [InlineData(new[] { "check", "--json" }, true)]
    [InlineData(new[] { "--json-file", "out.jsonl" }, true)]
    [InlineData(new[] { "--json-file=out.jsonl" }, true)]
    [InlineData(new[] { "--JSON" }, true)]
    [InlineData(new[] { "--jsonx" }, false)]
    [InlineData(new[] { "--jsonfile" }, false)]
    [InlineData(new[] { "--json-schema" }, false)]
    [InlineData(new[] { "--fix", "--non-interactive" }, false)]
    [InlineData(new string[0], false)]
    public void Detects_Only_The_Supported_Flags(string[] args, bool expected)
        => Assert.Equal(expected, JsonlOutput.IsStructuredOutputRequested(args));

    [Fact]
    public void Null_Args_Are_Not_Structured_Mode()
        => Assert.False(JsonlOutput.IsStructuredOutputRequested(null));
}

public class BuildReportTests
{
    [Fact]
    public void Summary_Counts_Every_Status_Bucket()
    {
        var checks = new[]
        {
            new HealthCheck { Id = "a", Status = JsonlOutput.StatusOk },
            new HealthCheck { Id = "b", Status = JsonlOutput.StatusOk },
            new HealthCheck { Id = "c", Status = JsonlOutput.StatusWarning },
            new HealthCheck { Id = "d", Status = JsonlOutput.StatusError },
            new HealthCheck { Id = "e", Status = JsonlOutput.StatusSkipped },
        };

        var report = JsonlOutput.BuildReport("1.2.3", "unhealthy", checks, reason: null);

        Assert.Equal("1.2.3", report.ToolVersion);
        Assert.Equal("unhealthy", report.Status);
        Assert.Null(report.Reason);
        Assert.Equal(5, report.Summary.Total);
        Assert.Equal(2, report.Summary.Ok);
        Assert.Equal(1, report.Summary.Warning);
        Assert.Equal(1, report.Summary.Error);
        Assert.Equal(1, report.Summary.Skipped);
    }

    [Fact]
    public void Abnormal_End_Carries_A_Reason()
    {
        var report = JsonlOutput.BuildReport("1.2.3", "unhealthy", [], reason: "canceled");
        Assert.Equal("canceled", report.Reason);
    }
}

/// <summary>
/// Classes in this collection mutate JsonlOutput's shared static state; the collection
/// forces xUnit to run them sequentially instead of in parallel with each other.
/// </summary>
[CollectionDefinition("JsonlOutputState")]
public class JsonlOutputStateCollection
{
}

/// <summary>
/// Sink behavior. These mutate JsonlOutput's static state and restore the disabled state.
/// </summary>
[Collection("JsonlOutputState")]
public class JsonlOutputSinkTests
{
    [Fact]
    public void Emit_Is_A_Noop_When_Disabled()
    {
        JsonlOutput.Init(null, null);
        Assert.False(JsonlOutput.Enabled);

        JsonlOutput.Emit(new CheckupStartedEvent { Id = "x", Name = "X" });
    }

    [Fact]
    public void JsonFile_Sink_Appends_One_Parseable_Line_Per_Event()
    {
        var fileName = $"unocheck-test-{Guid.NewGuid():n}.jsonl";
        var path = Path.Join(Path.GetTempPath(), Path.GetFileName(fileName));
        try
        {
            JsonlOutput.Init(stdout: null, filePath: path);
            Assert.True(JsonlOutput.Enabled);

            JsonlOutput.Emit(new CheckupStartedEvent { Id = "a", Name = "A" });
            JsonlOutput.Emit(new FixResultEvent { Id = "a", Success = true });

            JsonlOutput.Init(null, null); // release the writer before reading

            var lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.Equal("checkup_started",
                JsonDocument.Parse(lines[0]).RootElement.GetProperty("type").GetString());
            Assert.Equal("fix_result",
                JsonDocument.Parse(lines[1]).RootElement.GetProperty("type").GetString());
        }
        finally
        {
            JsonlOutput.Init(null, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void Stdout_Sink_Writes_To_The_Captured_Writer()
    {
        using var writer = new StringWriter();
        try
        {
            JsonlOutput.Init(writer, null);
            JsonlOutput.Emit(new CheckupStartedEvent { Id = "a", Name = "A" });

            var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
            Assert.Single(lines);
            Assert.Equal("checkup_started",
                JsonDocument.Parse(lines[0]).RootElement.GetProperty("type").GetString());
        }
        finally
        {
            JsonlOutput.Init(null, null);
        }
    }

    [Fact]
    public void CorrelationId_Is_Regenerated_Per_Run()
    {
        try
        {
            JsonlOutput.Init(null, null);
            var first = JsonlOutput.CorrelationId;

            JsonlOutput.Init(null, null);
            Assert.NotEqual(first, JsonlOutput.CorrelationId);
        }
        finally
        {
            JsonlOutput.Init(null, null);
        }
    }

    [Fact]
    public void Host_Supplied_CorrelationId_Is_Used_Verbatim()
    {
        // A host launching an elevated fix child passes its own id so both
        // processes report as one logical run.
        using var writer = new StringWriter();
        try
        {
            JsonlOutput.Init(writer, null, correlationId: "host-run-42");
            Assert.Equal("host-run-42", JsonlOutput.CorrelationId);

            JsonlOutput.Emit(new CheckupStartedEvent { Id = "a", Name = "A" });
            var json = JsonDocument.Parse(writer.ToString().Trim()).RootElement;
            Assert.Equal("host-run-42", json.GetProperty("correlation_id").GetString());
        }
        finally
        {
            JsonlOutput.Init(null, null);
        }
    }

    [Fact]
    public void Existing_File_At_JsonFile_Path_Is_Refused()
    {
        // The sink only writes files it created itself: a pre-existing file could be
        // stale output from a previous run — or a link planted at a caller-chosen path
        // while this process runs elevated.
        var path = Path.Join(Path.GetTempPath(), $"unocheck-test-{Guid.NewGuid():n}.jsonl");
        using var capturedError = new StringWriter();
        var originalError = Console.Error;
        try
        {
            File.WriteAllText(path, "stale");
            Console.SetError(capturedError);

            JsonlOutput.Init(stdout: null, filePath: path);

            Assert.False(JsonlOutput.Enabled);
            Assert.Contains("file sink disabled", capturedError.ToString());
            Assert.Equal("stale", File.ReadAllText(path));
        }
        finally
        {
            Console.SetError(originalError);
            JsonlOutput.Init(null, null);
            File.Delete(path);
        }
    }

    [Fact]
    public void Uncreatable_JsonFile_Disables_The_File_Sink_Instead_Of_Failing()
    {
        using var capturedError = new StringWriter();
        var originalError = Console.Error;
        try
        {
            Console.SetError(capturedError);

            // A directory path is not a creatable file on any OS.
            JsonlOutput.Init(stdout: null, filePath: Path.GetTempPath());

            Assert.False(JsonlOutput.Enabled);

            JsonlOutput.Emit(new CheckupStartedEvent { Id = "x", Name = "X" });
        }
        finally
        {
            Console.SetError(originalError);
            JsonlOutput.Init(null, null);
        }
    }
}

file class FakeRemainingArguments : IRemainingArguments
{
    public ILookup<string, string?> Parsed { get; } = Array.Empty<string>().ToLookup(s => s, s => (string?)null);
    public IReadOnlyList<string> Raw { get; } = [];
}

/// <summary>
/// End-to-end: runs the real command and asserts the three guarantees the contract
/// sells — every stdout line parses as JSON, the stream ends with a report, and the
/// wiring (Console redirect, event emission) is actually connected.
/// </summary>
[Collection("JsonlOutputState")]
public class CheckCommandEndToEndTests
{
    static async Task<(int ExitCode, string[] StdoutLines)> RunAsync(CheckSettings settings)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var originalConsole = AnsiConsole.Console;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);

            var command = new CheckCommand();
            var context = new CommandContext([], new FakeRemainingArguments(), "check", null);
            var exit = await command.ExecuteAsync(context, settings);

            var lines = stdout.ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .ToArray();

            return (exit, lines);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            JsonlOutput.Init(null, null);
            AnsiConsole.Console = originalConsole;
        }
    }

    [Fact]
    public async Task Json_Run_Emits_Pure_Jsonl_Ending_With_A_Report()
    {
        // A fake checkup keeps this hermetic (no machine probing) and deterministic, so the
        // assertions can pin the exact stream. The registry is append-only: use a unique id.
        var id = $"e2e-{Guid.NewGuid():n}";
        CheckupManager.RegisterCheckups(new FakeCheckup(id));

        var (exit, lines) = await RunAsync(new CheckSettings
        {
            Json = true,
            NonInteractive = true,
            Only = [id],
        });

        // Parse throws on any non-JSON stdout line — the purity guarantee.
        var events = lines.Select(l => JsonDocument.Parse(l).RootElement).ToArray();
        Assert.Equal(
            ["run_started", "checkup_started", "checkup_result", "report"],
            events.Select(e => e.GetProperty("type").GetString()).ToArray());

        var report = events[^1].GetProperty("report");
        Assert.Equal("healthy", report.GetProperty("status").GetString());
        Assert.Equal(1, report.GetProperty("summary").GetProperty("total").GetInt32());
        Assert.Equal(1, report.GetProperty("summary").GetProperty("ok").GetInt32());
        Assert.Equal(id, report.GetProperty("checks")[0].GetProperty("id").GetString());
        Assert.Equal(0, exit);
    }

    [Fact]
    public async Task Unknown_Only_Id_Fails_The_Run_And_Still_Emits_A_Report()
    {
        var (exit, lines) = await RunAsync(new CheckSettings
        {
            Json = true,
            NonInteractive = true,
            Only = ["definitely-not-a-checkup-xyz"],
        });

        Assert.Equal(1, exit);
        Assert.NotEmpty(lines);

        var last = JsonDocument.Parse(lines[^1]).RootElement;
        Assert.Equal("report", last.GetProperty("type").GetString());
        Assert.Equal("unhealthy", last.GetProperty("report").GetProperty("status").GetString());
        Assert.Contains("unknown checkup id", last.GetProperty("report").GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Uncreatable_Only_Sink_Fails_Fast_Instead_Of_Running_Blind()
    {
        // If the only requested sink cannot be created, the run must not proceed
        // emitting nothing while a host tails a file that never gets written.
        var path = Path.Join(Path.GetTempPath(), $"unocheck-e2e-{Guid.NewGuid():n}.jsonl");
        try
        {
            File.WriteAllText(path, "stale");

            var (exit, lines) = await RunAsync(new CheckSettings
            {
                JsonFile = path,
                NonInteractive = true,
                Only = ["openjdk"],
            });

            Assert.Equal(-1, exit);
            Assert.Empty(lines); // no stdout sink was requested, and nothing ran
            Assert.Equal("stale", File.ReadAllText(path)); // pre-existing file untouched
        }
        finally
        {
            File.Delete(path);
        }
    }
}

[Collection("JsonlOutputState")]
public class JsonlOutputStdoutFailureTests
{
    private sealed class ThrowingWriter : StringWriter
    {
        public override void WriteLine(string? value) => throw new IOException("pipe closed");
    }

    [Fact]
    public void Broken_Stdout_Pipe_Disables_The_Sink_Instead_Of_Aborting()
    {
        var originalError = Console.Error;
        using var capturedError = new StringWriter();
        try
        {
            Console.SetError(capturedError);
            JsonlOutput.Init(new ThrowingWriter(), null);

            // Must not throw out of Emit; the sink disables itself.
            JsonlOutput.Emit(new CheckupStartedEvent { Id = "x", Name = "X" });

            Assert.False(JsonlOutput.Enabled);
            Assert.Contains("sink disabled", capturedError.ToString());
        }
        finally
        {
            Console.SetError(originalError);
            JsonlOutput.Init(null, null);
        }
    }
}
