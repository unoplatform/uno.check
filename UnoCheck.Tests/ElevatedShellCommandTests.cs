using DotNetCheck;

namespace UnoCheck.Tests;

/// <summary>
/// The sudo fallback copies into protected destinations. Prefixing the chained command with
/// <c>sudo</c> elevates only its first half, so the copy runs unprivileged and fails against
/// exactly the destinations the fallback exists for.
/// </summary>
public class BuildElevatedShellCommandTests
{
    [Fact]
    public void Sudo_Covers_The_Whole_Chain_Not_Just_Its_First_Command()
    {
        var command = Util.BuildElevatedShellCommand("mkdir -p /usr/local/dest && cp -pP /tmp/src /usr/local/dest/file");

        // The shell is what runs under sudo, so both halves of the && chain are elevated.
        Assert.StartsWith($"sudo {ShellProcessRunner.MacOSShell} -c ", command);
        Assert.DoesNotContain("&& cp", command[..command.IndexOf("-c ", StringComparison.Ordinal)]);
    }

    [Fact]
    public void The_Chain_Is_Quoted_As_A_Single_Argument()
    {
        var command = Util.BuildElevatedShellCommand("mkdir -p /a && cp /b /a/c");

        var quoted = command[(command.IndexOf("-c ", StringComparison.Ordinal) + 3)..];

        Assert.StartsWith("'", quoted);
        Assert.EndsWith("'", quoted);
    }

    [Fact]
    public void Embedded_Quotes_Cannot_Escape_The_Chain()
    {
        var command = Util.BuildElevatedShellCommand("cp '/tmp/a b' \"/dest/$(whoami)\"");

        Assert.DoesNotContain("$(whoami)", command[..command.IndexOf("-c ", StringComparison.Ordinal)]);
        // PosixShellQuote closes the block, escapes the quote and reopens it.
        Assert.Contains("'\"'\"'", command);
    }
}
