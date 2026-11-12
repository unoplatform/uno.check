using DotNetCheck;

namespace UnoCheck.Tests;

/// <summary>
/// With --non-interactive, an out-of-date uno-check still showed the update prompt, which throws without a terminal
/// ("Cannot show selection prompt since the current terminal isn't interactive") and ended the run with exit code 255.
/// </summary>
public class ToolUpdaterTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void TheUpdatePromptIsOnlyShownInInteractiveRuns(bool nonInteractive, bool ci, bool prompts)
    {
        var settings = new CheckSettings { NonInteractive = nonInteractive, CI = ci };

        Assert.Equal(prompts, ToolUpdater.CanPrompt(settings));
    }
}
