using System.Text.Json;
using DotNetCheck;
using DotNetCheck.Json;
using Spectre.Console;
using AnsiConsole = Spectre.Console.AnsiConsole;

namespace UnoCheck.Tests;

/// <summary>
/// Redirects the three streams a run can write to — stdout, stderr and Spectre's console —
/// into strings, with Spectre deliberately pointed at stdout so that a claim which fails to
/// move it shows up as text on the event stream.
/// </summary>
file sealed class ConsoleCapture : IDisposable
{
    readonly TextWriter _originalOut = Console.Out;
    readonly TextWriter _originalError = Console.Error;
    readonly IAnsiConsole _originalConsole = AnsiConsole.Console;
    readonly StringWriter _stdout = new();
    readonly StringWriter _stderr = new();

    public ConsoleCapture()
    {
        // A claim leaked by an earlier test would make ClaimStdout a no-op here.
        JsonlOutput.ReleaseStdout();

        Console.SetOut(_stdout);
        Console.SetError(_stderr);
        AnsiConsole.Console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            Out = new AnsiConsoleOutput(_stdout),
        });
    }

    public string Stdout => _stdout.ToString();
    public string Stderr => _stderr.ToString();

    public void Dispose()
    {
        JsonlOutput.ReleaseStdout();
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        JsonlOutput.Init(null, null);
        AnsiConsole.Console = _originalConsole;

        // The writers are deliberately not disposed: the command app can still hold a
        // console from an earlier in-process run, and a closed StringWriter turns that into
        // a crash in the next test instead of harmless output nobody reads. A StringWriter
        // owns no resource, so leaving it open costs nothing.
    }
}

/// <summary>
/// <c>--json</c> makes stdout the event stream, so nothing else may reach it. The command
/// infrastructure captures stdout when it is constructed and writes its own failures there —
/// an unparseable option, a manifest that does not exist — so the claim has to happen before
/// the app is built. Claiming inside the command left a JSON report followed by plain text on
/// the same stream, which no JSON reader survives.
/// </summary>
[Collection("JsonlOutputState")]
public class StdoutClaimTests
{
    [Theory]
    [InlineData(new[] { "--json" }, true)]
    [InlineData(new[] { "check", "--json" }, true)]
    [InlineData(new[] { "--JSON" }, true)]
    // --json-file writes events to a file and leaves stdout as ordinary human output.
    [InlineData(new[] { "--json-file", "out.jsonl" }, false)]
    [InlineData(new[] { "--jsonx" }, false)]
    [InlineData(new string[0], false)]
    public void Only_The_Stdout_Stream_Flag_Claims_Stdout(string[] args, bool expected)
        => Assert.Equal(expected, JsonlOutput.IsStdoutStreamRequested(args));

    [Fact]
    public void Null_Args_Do_Not_Claim_Stdout()
        => Assert.False(JsonlOutput.IsStdoutStreamRequested(null));

    [Fact]
    public void Claiming_Sends_Human_And_Spectre_Output_To_Stderr()
    {
        using var capture = new ConsoleCapture();

        JsonlOutput.ClaimStdout();

        Console.WriteLine("human output");
        AnsiConsole.WriteLine("spectre output");

        Assert.Equal(string.Empty, capture.Stdout);
        Assert.Contains("human output", capture.Stderr);
        Assert.Contains("spectre output", capture.Stderr);
    }

    [Fact]
    public void The_Reserved_Writer_Is_The_Real_Stdout()
    {
        using var capture = new ConsoleCapture();

        JsonlOutput.ClaimStdout();

        // The event stream is emitted through the reserved writer, so it has to be the
        // stdout that was current before the claim, not the stderr it was swapped for.
        JsonlOutput.ReservedStdout!.WriteLine("{\"type\":\"probe\"}");

        Assert.Contains("probe", capture.Stdout);
        Assert.DoesNotContain("probe", capture.Stderr);
    }

    [Fact]
    public void Claiming_Twice_Does_Not_Stack_Redirects()
    {
        using var capture = new ConsoleCapture();

        JsonlOutput.ClaimStdout();
        var reserved = JsonlOutput.ReservedStdout;
        JsonlOutput.ClaimStdout();

        // A second claim must not reserve stderr and start echoing the stream into itself.
        Assert.Same(reserved, JsonlOutput.ReservedStdout);

        JsonlOutput.ReservedStdout!.WriteLine("{\"type\":\"probe\"}");
        Console.WriteLine("human output");

        Assert.Contains("probe", capture.Stdout);
        Assert.DoesNotContain("human output", capture.Stdout);
    }
}

/// <summary>
/// The failures that reach the user before any checkup runs. These go through the real
/// command app, built in the same order <c>Main</c> builds it — after the claim — because
/// the app captures whatever stdout is current at construction.
/// </summary>
[Collection("JsonlOutputState")]
public class CommandAppFailureOutputTests
{
    static async Task<int> RunAsync(params string[] args)
    {
        if (JsonlOutput.IsStdoutStreamRequested(args))
            JsonlOutput.ClaimStdout();

        return await DotNetCheck.Program.CreateCommandApp()
            .RunAsync(DotNetCheck.Program.BuildFinalArgs(args));
    }

    static void AssertEveryLineIsJson(string stdout)
    {
        var lines = stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Where(line => line.Length > 0);

        foreach (var line in lines)
        {
            // Throws on the plain-text error that used to follow the report.
            JsonDocument.Parse(line);
        }
    }

    [Fact]
    public async Task A_Missing_Manifest_Leaves_The_Event_Stream_Parseable()
    {
        using var capture = new ConsoleCapture();
        var missing = Path.Combine(Path.GetTempPath(), $"unocheck-missing-{Guid.NewGuid():n}.json");

        await RunAsync("--json", "--manifest", missing);

        AssertEveryLineIsJson(capture.Stdout);
    }

    [Fact]
    public async Task An_Unparseable_Option_Writes_Nothing_To_The_Event_Stream()
    {
        using var capture = new ConsoleCapture();

        // '--only' with no value fails inside the command app's own parser, before any
        // command runs — the case the in-command redirect could never reach.
        var exit = await RunAsync("--json", "--only");

        Assert.NotEqual(0, exit);
        Assert.Equal(string.Empty, capture.Stdout);
        Assert.NotEqual(string.Empty, capture.Stderr);
    }
}

/// <summary>Minimal checkup so the catalog has a known entry to look for.</summary>
file class CatalogProbeCheckup : DotNetCheck.Models.Checkup
{
    readonly string _id;

    public CatalogProbeCheckup(string id) => _id = id;

    public override string Id => _id;
    public override string Title => $"Catalog probe {_id}";

    public override Task<DotNetCheck.Models.DiagnosticResult> Examine(DotNetCheck.Models.SharedState history)
        => Task.FromResult(DotNetCheck.Models.DiagnosticResult.Ok(this));
}

/// <summary>
/// The catalog is the first thing a host asks for, so it has the same stdout discipline as a
/// check run: the JSON line on stdout, the version banner and everything else on stderr.
/// </summary>
[Collection("JsonlOutputState")]
public class ListCatalogOutputTests
{
    [Fact]
    public async Task The_Catalog_Lands_On_Stdout_And_The_Banner_Does_Not()
    {
        using var capture = new ConsoleCapture();

        var probeId = $"catalog-probe-{Guid.NewGuid():n}";
        DotNetCheck.Models.CheckupManager.RegisterCheckups(new CatalogProbeCheckup(probeId));

        string[] args = ["list", "--json"];
        JsonlOutput.ClaimStdout();

        var exit = await DotNetCheck.Program.CreateCommandApp()
            .RunAsync(DotNetCheck.Program.BuildFinalArgs(args));

        Assert.Equal(0, exit);

        var lines = capture.Stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToArray();

        // Exactly one line, and it parses: reading Console.Out after the claim would have
        // sent this to stderr and left a host waiting on an empty stream.
        var only = Assert.Single(lines);
        var root = JsonDocument.Parse(only).RootElement;
        Assert.Equal("checkup_catalog", root.GetProperty("type").GetString());
        Assert.Contains(
            root.GetProperty("checkups").EnumerateArray(),
            c => c.GetProperty("id").GetString() == probeId);
    }
}
