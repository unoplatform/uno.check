using DotNetCheck.Cli;
using Spectre.Console;
using Status = DotNetCheck.Models.Status;

namespace UnoCheck.Tests;

/// <summary>
/// Validates that dynamic text containing Spectre.Console markup-like characters
/// (brackets, etc.) is properly escaped before being passed to AnsiConsole.MarkupLine.
/// This prevents crashes like "Could not find color or style" when file paths or
/// system messages contain square brackets (e.g., workload manifest paths on .NET 10+).
/// See: https://github.com/unoplatform/uno.check/issues/11
/// </summary>
public class SpectreMarkupEscapingTests
{
    /// <summary>
    /// Verifies that the given markup string can be parsed by Spectre.Console without throwing.
    /// </summary>
    private static void AssertMarkupParses(string markup)
    {
        var record = Record.Exception(() => AnsiConsole.Create(new AnsiConsoleSettings
        {
            Out = new AnsiConsoleOutput(TextWriter.Null)
        }).MarkupLine(markup));

        Assert.Null(record);
    }

    /// <summary>
    /// Reproduces the exact CI crash scenario: a .NET 10 workload manifest path
    /// containing text that Spectre.Console interprets as a style/color tag.
    /// </summary>
    [Theory]
    [InlineData("Checking sdk-manifests\\10.0.100\\microsoft.net.workload.mono.toolchain.current\\10.0.105\\WorkloadManifest.json")]
    [InlineData("Found [something] in path")]
    [InlineData("Path with [bold] brackets")]
    [InlineData("[red]not a style[/]")]
    [InlineData("C:\\hostedtoolcache\\windows\\dotnet\\sdk-manifests\\10.0.100\\microsoft.net.workload.mono.toolchain.current\\10.0.105\\WorkloadManifest.json")]
    public void BuildCheckupStatusMarkup_EscapesBracketsInMessages(string message)
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup(message, Status.Ok);
        AssertMarkupParses(markup);
    }

    [Theory]
    [InlineData(Status.Ok)]
    [InlineData(Status.Warning)]
    [InlineData(Status.Error)]
    [InlineData(null)]
    public void BuildCheckupStatusMarkup_AllStatuses_HandleBracketsWithoutCrashing(Status? status)
    {
        var message = "sdk-manifests\\10.0.100\\[toolchain]\\WorkloadManifest.json";
        var markup = CheckCommand.BuildCheckupStatusMarkup(message, status);
        AssertMarkupParses(markup);
    }

    [Fact]
    public void BuildCheckupStatusMarkup_PreservesMessageContent()
    {
        var message = "Workload [mono.toolchain] is installed";

        var markup = CheckCommand.BuildCheckupStatusMarkup(message, Status.Ok);

        // The escaped brackets should appear as literal text in output, not be stripped
        Assert.Contains("[[mono.toolchain]]", markup);
    }

    [Theory]
    [InlineData("Simple message without brackets")]
    [InlineData("Version 10.0.105 installed")]
    [InlineData("")]
    public void BuildCheckupStatusMarkup_SafeMessages_StillWork(string message)
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup(message, Status.Ok);
        AssertMarkupParses(markup);
    }

    [Fact]
    public void BuildCheckupStatusMarkup_ErrorStatus_ContainsRedMarkup()
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup("some error", Status.Error);
        Assert.StartsWith("[red]", markup);
    }

    [Fact]
    public void BuildCheckupStatusMarkup_WarningStatus_ContainsOrangeMarkup()
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup("some warning", Status.Warning);
        Assert.StartsWith("[darkorange3_1]", markup);
    }

    [Fact]
    public void BuildCheckupStatusMarkup_OkStatus_ContainsGreenMarkup()
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup("all good", Status.Ok);
        Assert.StartsWith("[green]", markup);
    }

    [Fact]
    public void BuildCheckupStatusMarkup_NullStatus_NoColorMarkup()
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup("info message", null);
        Assert.DoesNotContain("[green]", markup);
        Assert.DoesNotContain("[red]", markup);
        Assert.DoesNotContain("[darkorange3_1]", markup);
    }

    /// <summary>
    /// Validates that Markup.Escape properly handles the exact path pattern
    /// from the CI failure — the workload manifest path that contains text
    /// Spectre.Console would interpret as a style tag.
    /// </summary>
    [Fact]
    public void MarkupEscape_HandlesWorkloadManifestPath()
    {
        var path = @"C:\hostedtoolcache\windows\dotnet\sdk-manifests\10.0.100\microsoft.net.workload.mono.toolchain.current\[10.0.105]\WorkloadManifest.json";
        var escaped = Markup.Escape(path);

        // Brackets in the path should be doubled for escaping
        Assert.DoesNotContain("[", escaped.Replace("[[", ""));
        Assert.DoesNotContain("]", escaped.Replace("]]", ""));

        // Should render without throwing
        AssertMarkupParses(escaped);
    }

    /// <summary>
    /// Validates that messages from WSL environments containing bracket patterns
    /// (like [Argument] or [Argumento]) are also escaped — the original bug reports
    /// from issues #11 and #14.
    /// </summary>
    [Theory]
    [InlineData("dotnet : [Argument] The value is invalid")]
    [InlineData("dotnet : [Argumento] El valor no es válido")]
    public void BuildCheckupStatusMarkup_WslBracketPatterns_DoNotCrash(string message)
    {
        var markup = CheckCommand.BuildCheckupStatusMarkup(message, Status.Error);
        AssertMarkupParses(markup);
    }
}
