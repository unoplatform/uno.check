using System.ComponentModel;
using Uno.Check.Client;

namespace UnoCheck.Tests;

/// <summary>
/// Pins the command lines the client composes: which flags a run carries, and which ids are
/// allowed to reach a command line at all. Hosts execute these strings — sometimes elevated —
/// so a silent change here is a correctness *and* safety regression, not a cosmetic one.
/// </summary>
public class ClientArgumentTests
{
	private static UnoCheckRunOptions Options(
		string[]? targets = null,
		string[]? skips = null,
		bool preview = false,
		bool verbose = false,
		bool applyFixes = false)
		=> new(targets ?? [], skips ?? [], preview, verbose, applyFixes);

	/// <summary>
	/// Element-wise argument comparison. The IEnumerable cast disambiguates xUnit's
	/// ReadOnlySpan overload, which a collection expression also binds to.
	/// </summary>
	private static void AssertArguments(string[] expected, string[] actual)
		=> Assert.Equal(expected, (IEnumerable<string>)actual);

	[Fact]
	[Description("A default diagnosis stays non-interactive and structured: without those two flags the tool prompts and the host's JSONL stream never parses.")]
	public void BuildDiagnosisArguments_Default_IsNonInteractiveJson()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(UnoCheckRunOptions.Default, allowElevationPrompt: false);

		AssertArguments(["--non-interactive", "--json"], args);
	}

	[Fact]
	[Description("Each selected target repeats the --target flag; a bare space-separated list is ignored, so a single flag would silently widen the run.")]
	public void BuildDiagnosisArguments_RepeatsTargetFlagPerPlatform()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(
			Options(targets: ["wasm", "android"]),
			allowElevationPrompt: false);

		AssertArguments(["--non-interactive", "--json", "--target", "wasm", "--target", "android"], args);
	}

	[Fact]
	[Description("Excluded checkups become --skip so the tool reports them as skipped, rather than a host hiding rows the tool still ran.")]
	public void BuildDiagnosisArguments_ExcludedCheckupsBecomeSkip()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(
			Options(skips: ["androidsdk", "androidemulator"]),
			allowElevationPrompt: false);

		AssertArguments(
			["--non-interactive", "--json", "--skip", "androidsdk", "--skip", "androidemulator"],
			args);
	}

	[Fact]
	[Description("Channel and verbose selections reach the tool; a dropped --pre would diagnose stable while the host claims preview.")]
	public void BuildDiagnosisArguments_AppendsChannelAndVerbose()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(
			Options(preview: true, verbose: true),
			allowElevationPrompt: false);

		Assert.Contains("--pre", args);
		Assert.Contains("--verbose", args);
	}

	[Theory]
	[InlineData(true, true)]
	[InlineData(false, false)]
	[Description("A fixing run only asks for the platform authorization dialog where the host wants it — on Windows a host elevates a separate child instead.")]
	public void BuildDiagnosisArguments_ElevationPromptFollowsTheCallersDecision(bool allowElevationPrompt, bool expectFlag)
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(Options(applyFixes: true), allowElevationPrompt);

		Assert.Contains("--fix", args);
		Assert.Equal(expectFlag, args.Contains("--allow-elevation-prompt"));
	}

	[Fact]
	[Description("A plain diagnosis never carries --fix: examining must not mutate the machine.")]
	public void BuildDiagnosisArguments_WithoutApplyFixes_NeverFixes()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(Options(), allowElevationPrompt: true);

		Assert.DoesNotContain("--fix", args);
		Assert.DoesNotContain("--allow-elevation-prompt", args);
	}

	[Fact]
	[Description("An unsafe id is dropped rather than passed through: a smuggled flag would otherwise widen the invocation.")]
	public void BuildDiagnosisArguments_DropsUnsafeIds()
	{
		var args = UnoCheckProtocol.BuildDiagnosisArguments(
			Options(targets: ["wasm", "evil --fix"], skips: ["ok-id", "bad;id"]),
			allowElevationPrompt: false);

		AssertArguments(["--non-interactive", "--json", "--target", "wasm", "--skip", "ok-id"], args);
	}

	[Fact]
	[Description("A fix inherits the diagnosis's target/channel scope, so it never runs against a different manifest than the check that surfaced it.")]
	public void BuildFixArguments_InheritsTargetAndChannelScope()
	{
		var args = UnoCheckProtocol.BuildFixArguments(
			["windowshyperv"],
			Options(targets: ["wasm"], preview: true),
			allowElevationPrompt: false,
			jsonFilePath: null);

		AssertArguments(
			["--fix", "--non-interactive", "--only", "windowshyperv", "--target", "wasm", "--pre", "--json"],
			args);
	}

	[Fact]
	[Description("Every id in a batch is named with its own --only: one invocation is what holds a multi-fix batch to a single elevation prompt.")]
	public void BuildFixArguments_NamesEveryCheckupInOneInvocation()
	{
		var args = UnoCheckProtocol.BuildFixArguments(
			["windowslongpath", "windowshyperv"],
			Options(),
			allowElevationPrompt: false,
			jsonFilePath: null);

		AssertArguments(
			["--fix", "--non-interactive", "--only", "windowslongpath", "--only", "windowshyperv", "--json"],
			args);
	}

	[Fact]
	[Description("An id that fails the allow-list is dropped rather than smuggled onto an elevated command line.")]
	public void BuildFixArguments_DropsUnsafeIds()
	{
		var args = UnoCheckProtocol.BuildFixArguments(
			["unosdk", "a b && del"],
			Options(),
			allowElevationPrompt: false,
			jsonFilePath: null);

		Assert.Contains("unosdk", args);
		Assert.DoesNotContain("a b && del", args);
	}

	[Fact]
	[Description("Skip ids are deliberately not forwarded to a fix: a host's display filter must not exclude the very checkup the user chose to fix.")]
	public void BuildFixArguments_NeverForwardsSkipIds()
	{
		var args = UnoCheckProtocol.BuildFixArguments(
			["unosdk"],
			Options(skips: ["unosdk", "dotnet"]),
			allowElevationPrompt: false,
			jsonFilePath: null);

		Assert.DoesNotContain("--skip", args);
	}

	[Fact]
	[Description("An elevated child writes to a file instead of stdout, which cannot cross the privilege boundary — and must not also claim stdout.")]
	public void BuildFixArguments_WithFilePath_UsesFileTransportOnly()
	{
		var args = UnoCheckProtocol.BuildFixArguments(
			["windowslongpath"],
			Options(),
			allowElevationPrompt: false,
			jsonFilePath: @"C:\Temp\events.jsonl");

		Assert.Contains("--json-file", args);
		Assert.Contains(@"C:\Temp\events.jsonl", args);
		Assert.DoesNotContain("--json", args);
		Assert.DoesNotContain("--allow-elevation-prompt", args);
	}

	[Fact]
	[Description("The catalog call honors targets/channel — a host must be offered exactly the checkups the next run would examine.")]
	public void BuildCatalogArguments_HonorsTargetsAndChannel()
	{
		var args = UnoCheckProtocol.BuildCatalogArguments(Options(targets: ["ios"], preview: true, verbose: true));

		AssertArguments(["list", "--json", "--target", "ios", "--pre"], args);
	}

	[Fact]
	[Description("A scoped re-examination carries no --fix and names every id in one invocation, because process startup is a fixed per-invocation tax.")]
	public void BuildScopedCheckArguments_ScopesWithoutFixing()
	{
		var args = UnoCheckProtocol.BuildScopedCheckArguments(
			["git", "unosdk"],
			Options(targets: ["wasm"], skips: ["git"], applyFixes: true));

		AssertArguments(
			["--only", "git", "--only", "unosdk", "--non-interactive", "--json", "--target", "wasm"],
			args);
		Assert.DoesNotContain("--fix", args);
		Assert.DoesNotContain("--skip", args);
	}

	[Theory]
	[InlineData("openjdk", true)]
	[InlineData("dotnetworkloads-10.0.201", true)]
	[InlineData("https-dev-cert", true)]
	[InlineData("Vs_Win.Workloads", true)]
	[InlineData("id with spaces", false)]
	[InlineData("id\"quote", false)]
	[InlineData("id;semicolon", false)]
	[InlineData("id&&whoami", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	[Description("Ids reach a hand-quoted, sometimes elevated command line; anything carrying argument or shell metacharacters must be refused rather than escaped.")]
	public void IsSafeCheckupId_RefusesArgumentAndShellMetacharacters(string? id, bool expected)
		=> Assert.Equal(expected, UnoCheckProtocol.IsSafeCheckupId(id));

	[Theory]
	[InlineData(@"C:\Users\dev\feed", true)]
	[InlineData(@"C:\Users\Test User\local feed", true)]
	[InlineData("https://pkgs.example.com/v3/index.json", true)]
	[InlineData(@"C:\feed""--evil", false)]
	[InlineData("   ", false)]
	[InlineData(null, false)]
	[Description("Values a host embeds in a hand-quoted elevated command line: paths with spaces must keep working, but an embedded quote would break the argument boundary.")]
	public void IsSafeEngineArgument_AllowsSpacesButRefusesQuotes(string? value, bool expected)
		=> Assert.Equal(expected, UnoCheckProtocol.IsSafeEngineArgument(value));
}
