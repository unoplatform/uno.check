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
	/// The CLI's first run prints an ASCII-art banner and a telemetry notice ahead of the
	/// version on the same stream. Joining every line put the whole banner into the status
	/// message, so the reported version is read from the one line that is a version alone.
	/// </summary>
	[Theory]
	[InlineData("0.7.0")]
	[InlineData("1.0.0-preview.3")]
	public async Task TheVersionIsReadPastTheFirstRunBanner(string version)
	{
		var banner = new[]
		{
			@"  __      _(_)_ __   __ _ _ __  _ __      ___| (_)",
			$" Windows App Development CLI - Version {version}",
			"Welcome to the Windows App Development CLI! By using this tool, you agree to the",
			"collection of anonymous usage data to help improve the product.",
			string.Empty,
			version,
		};

		var reported = string.Empty;
		var sut = new WinAppCliCheckup(() => Task.FromResult(Result(0, banner)));
		sut.OnStatusUpdated += (_, e) => reported = e.Message;

		var result = await sut.Examine(State);

		Assert.Equal(Status.Ok, result.Status);
		Assert.Equal($"WinApp CLI {version} is installed.", reported);
	}

	/// <summary>
	/// Output with no version line at all still reads as installed rather than dragging
	/// whatever the CLI printed into the status message.
	/// </summary>
	[Fact]
	public async Task OutputWithNoVersionLineStillReadsAsInstalled()
	{
		var reported = string.Empty;
		var sut = new WinAppCliCheckup(() => Task.FromResult(Result(0, "nothing version-shaped here")));
		sut.OnStatusUpdated += (_, e) => reported = e.Message;

		await sut.Examine(State);

		Assert.Equal("WinApp CLI is installed.", reported);
	}

	/// <summary>
	/// Same gate as EdgeWebView2Checkup: a build agent does not need the CLI to compile,
	/// and examining it there sends every Windows CI run through a winget network install.
	/// </summary>
	[Fact]
	public void CiIsNotExamined()
	{
		var sut = new WinAppCliCheckup { Manifest = ManifestTargetingWindows() };

		var developerMachine = new SharedState();
		Assert.True(sut.ShouldExamine(developerMachine));

		var buildAgent = new SharedState();
		buildAgent.SetEnvironmentVariable("CI", "true");
		Assert.False(sut.ShouldExamine(buildAgent));
	}

	[Fact]
	public void AManifestThatDoesNotTargetWindowsIsNotExamined()
	{
		var sut = new WinAppCliCheckup { Manifest = new DotNetCheck.Manifest.Manifest { Check = new() } };

		Assert.False(sut.ShouldExamine(new SharedState()));
	}

	private static DotNetCheck.Manifest.Manifest ManifestTargetingWindows()
		=> new() { Check = new() { VSWin = new DotNetCheck.Manifest.MinExactVersion() } };

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
