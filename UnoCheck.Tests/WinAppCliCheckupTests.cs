using System.ComponentModel;

using DotNetCheck;
using DotNetCheck.Checkups;
using DotNetCheck.Models;
using DotNetCheck.Solutions;

namespace UnoCheck.Tests;

/// <summary>
/// The checkup probes for the CLI by starting <c>winapp --version</c>. On the machine it
/// exists for - one where the CLI was never installed - Process.Start throws
/// Win32Exception/ERROR_FILE_NOT_FOUND out of the runner's constructor, before any exit
/// code exists. Letting that escape turned the one case worth fixing into a bare error
/// with no suggestion attached, so the install never got offered.
/// </summary>
public class WinAppCliCheckupTests
{
	private static readonly SharedState State = new();

	[Fact]
	public async Task AMissingExecutableOffersTheInstall()
	{
		var sut = new WinAppCliCheckup(() => throw new Win32Exception(2));

		var result = await sut.Examine(State);

		Assert.Equal(Status.Error, result.Status);
		Assert.True(result.HasSuggestion);
		Assert.IsType<WinAppCliInstallSolution>(Assert.Single(result.Suggestion.Solutions));
	}

	[Fact]
	public async Task ANonZeroExitOffersTheInstall()
	{
		var sut = new WinAppCliCheckup(() => Task.FromResult(Result(7)));

		var result = await sut.Examine(State);

		Assert.Equal(Status.Error, result.Status);
		Assert.True(result.HasSuggestion);
		Assert.IsType<WinAppCliInstallSolution>(Assert.Single(result.Suggestion.Solutions));
	}

	[Fact]
	public async Task AnInstalledCliPasses()
	{
		var sut = new WinAppCliCheckup(() => Task.FromResult(Result(0, "1.2.3")));

		var result = await sut.Examine(State);

		Assert.Equal(Status.Ok, result.Status);
		Assert.False(result.HasSuggestion);
	}

	/// <summary>
	/// Only "no such executable" means the CLI is absent. A start failure for any other
	/// reason - a denied execution policy, a corrupt image - is a different problem, and
	/// reporting it as a missing CLI would send the user to install something they have.
	/// </summary>
	[Fact]
	public async Task AnUnrelatedStartFailureIsNotReportedAsAMissingCli()
	{
		var sut = new WinAppCliCheckup(() => throw new Win32Exception(5));

		await Assert.ThrowsAsync<Win32Exception>(() => sut.Examine(State));
	}

	/// <summary>
	/// The same guarantee against the real runner rather than the injected probe, since the
	/// regression lived in how the process was started. Whichever way this machine answers,
	/// an error carries the fix with it.
	/// </summary>
	[Fact]
	public async Task TheRealProbeAlwaysYieldsAnActionableDiagnosis()
	{
		if (!Util.IsWindows)
		{
			return;
		}

		var result = await new WinAppCliCheckup().Examine(State);

		if (result.Status == Status.Error)
		{
			Assert.True(result.HasSuggestion);
			Assert.True(result.Suggestion.HasSolution);
		}
	}

	private static ShellProcessRunner.ShellProcessResult Result(int exitCode, params string[] standardOutput)
		=> new([.. standardOutput], [], exitCode);
}
