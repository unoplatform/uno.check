# Mac validation: Xcode check per target framework (#618)

Validation steps for branch `dev/ap/xcode-previous-tfm-618`, to run on a Mac, for example with Claude Code. The change can't be exercised on Windows, and CI's macOS jobs pass `--skip xcode`.

**For Claude:** read the whole file before running anything. This is validation only. Don't change code, commit or push. Ask the person before any `sudo` command or anything else that changes the machine, such as `xcode-select -s` or installing Xcode. Run uno-check without `--fix`. At the end, fill in the report (section 5) and show it to the person. Post it to GitHub only if they ask.

## What changed

Each .NET for iOS, tvOS, Mac Catalyst or macOS SDK pack accepts only one Xcode major.minor, its `_RecommendedXcodeVersion`. The .NET 11.0.100 Apple workloads (27.2.11039) build `net11.0-*` apps with packs that need Xcode 27.2. They build `net10.0-*` apps with their `net10.0_26.0` packs (26.0.11017), which need Xcode 26.0. uno-check used to check only Xcode 27.2 or later, so `--tfm net10.0-ios` passed with Xcode 27.2 and the build then failed. Now:

- When every Apple target framework passed with `--tfm` is `net10.0`, uno-check requires Xcode 26.0 (same major.minor).
- `net11.0` Apple target frameworks keep the Xcode 27.2+ check.
- `net10.0` and `net11.0` Apple target frameworks together: Xcode 27.2+ is checked, plus a warning that the `net10.0` ones need Xcode 26.0.
- Without `--tfm`: Xcode 27.2+ is checked, plus an informational line about `net10.0` targets.

## 1. Record the machine

```bash
sw_vers
xcode-select -p
xcodebuild -version
for app in /Applications/Xcode*.app; do echo "$app: $(/usr/libexec/PlistBuddy -c 'Print CFBundleShortVersionString' "$app/Contents/Info.plist")"; done
dotnet --list-sdks
dotnet workload list
```

Expected setup: Xcode 27.2 selected and the .NET SDK 11.0.100 installed. Note whether an Xcode 26.0 is installed side by side, and its path. Scenarios S2 and S9 need it, and S1 changes when it's there.

Keep the output of `xcode-select -p`. If the selection changes later, restore it at the end.

## 2. Get the uno-check build from this branch

Install it into a temporary folder, so the machine's `uno-check` is left alone.

**Option A (preferred): the NuGet package from the PR's CI run.**

```bash
WORK=$(mktemp -d)
BRANCH=dev/ap/xcode-previous-tfm-618
HEAD_SHA=$(gh api repos/unoplatform/uno.check/branches/$BRANCH --jq .commit.sha)
RUN_ID=$(gh run list -R unoplatform/uno.check --branch $BRANCH --workflow CI --limit 5 --json databaseId,headSha \
  --jq "map(select(.headSha == \"$HEAD_SHA\"))[0].databaseId")
echo "head $HEAD_SHA, run $RUN_ID"
gh run download "$RUN_ID" -R unoplatform/uno.check -n NuGet -D "$WORK/nuget"
PKG=$(ls "$WORK"/nuget/Uno.Check.*.nupkg | grep -v symbols | head -1)
VERSION=$(basename "$PKG" .nupkg | sed 's/^Uno\.Check\.//')
dotnet tool install --tool-path "$WORK/tool" --add-source "$WORK/nuget" --version "$VERSION" Uno.Check
UNOCHECK="$WORK/tool/uno-check"
```

If `RUN_ID` is empty, the CI run for the branch head hasn't started yet, or its Build job hasn't uploaded the `NuGet` artifact yet. Wait and retry, or use option B.

**Option B: build from source.**

```bash
WORK=$(mktemp -d)
git clone --branch dev/ap/xcode-previous-tfm-618 https://github.com/unoplatform/uno.check.git "$WORK/src"
dotnet build "$WORK/src/UnoCheck/UnoCheck.csproj" -c Release -f net6.0
UNOCHECK="dotnet $WORK/src/UnoCheck/bin/Release/net6.0/UnoCheck.dll"
```

Every scenario runs this base command (`$UNOCHECK` is unquoted on purpose, so option B's two words work), followed by its options:

```bash
$UNOCHECK --only xcode --non-interactive --verbose
```

The first lines of the output show the uno-check version. The checkup line starts with an icon and ends with `Checkup...`. Status icons depend on the terminal, so compare the text and the kind of line (OK, warning, error or information), not the icon.

If the output says the selected Xcode is missing the iOS SDK runtime, that check is older than this change. Note it in the report and continue.

## 3. Scenarios

Unless a scenario says otherwise, Xcode 27.2 is selected. `<build>` is the Xcode build number, for example `27B52`.

| # | Options | Needs | Expected |
| --- | --- | --- | --- |
| S1 | `--tfm net10.0-ios` | No Xcode 26.0 installed | Checkup line: `Required Xcode 26.0 for net10.0-ios Checkup...`. Error: `Xcode 27.2 (<build>) is installed, but Xcode 26.0 is required. net10.0-ios builds with this .NET SDK's net10.0 Apple SDK packs, which require Xcode 26.0. To use Xcode 27.2, move to net11.0-ios.` Any other installed Xcode is listed too. Recommendation: `Download Xcode 26.0 from https://developer.apple.com/download/all/`. |
| S2 | `--tfm net10.0-ios` | Xcode 26.0 installed, not selected | Error: `No Xcode.app or an incompatible Xcode.app version is selected, but one was found at (<path of Xcode 26.0>). net10.0-ios builds with this .NET SDK's net10.0 Apple SDK packs, which require Xcode 26.0. To use Xcode 27.2, move to net11.0-ios.` Recommendation: `Run xcode-select -s <Path>`. Don't apply it. |
| S3 | `--tfm net11.0-ios` | | Checkup line: `Required Xcode 27.2 (newer version might not be supported) Checkup...`. OK: `Xcode.app (27.2 <build>)`. No other line. |
| S4 | no `--tfm` | | As S3, plus an information line: `With this .NET SDK, net10.0 Apple targets (iOS, tvOS, Mac Catalyst) need Xcode 26.0, and the selected Xcode 27.2 builds net11.0 ones. Pass your app's target frameworks with --tfm to check them.` The checkup still passes. |
| S5 | `--tfm net10.0-ios --tfm net11.0-ios` | | Checkup line for 27.2, as S3. OK: `Xcode.app (27.2 <build>)`. Warning: `With this .NET SDK, net10.0-ios needs Xcode 26.0, so the selected Xcode 27.2 can't build it. Move to net11.0-ios, or select the Xcode it needs when you build it.` The run reports a warning, not an error. |
| S6 | `--tfm net10.0-tvos --tfm net10.0-maccatalyst` | No Xcode 26.0 installed (otherwise expect S2's text) | Checkup line: `Required Xcode 26.0 for net10.0-tvos, net10.0-maccatalyst Checkup...`. The error's reason reads `net10.0-tvos, net10.0-maccatalyst build with this .NET SDK's net10.0 Apple SDK packs, which require Xcode 26.0. To use Xcode 27.2, move to net11.0-tvos, net11.0-maccatalyst.` |
| S7 | `--tfm net10.0-android` | | As S3: Xcode 27.2+ is checked and passes. No information line, because `--tfm` was given. |
| S8 | `--tfm net10.0-ios --json > "$WORK/s8.jsonl"` | No Xcode 26.0 installed | The JSON lines (one event per line) include the `xcode` check with an error status, and its message contains S1's text, including the `net10.0-ios builds with this .NET SDK's ...` reason. If Xcode 26.0 is installed (S2's case), note what the message says instead: that path doesn't put its reason on the result, and that's older than this change. |
| S9 | `--tfm net10.0-ios`, then `--tfm net11.0-ios`, with Xcode 26.0 selected | Xcode 26.0 installed | `net10.0-ios`: checkup line `Required Xcode 26.0 for net10.0-ios Checkup...`, and OK: `Xcode.app (26.0 <build>)`. `net11.0-ios`: an error for Xcode 27.2. The text depends on what's installed: either `Xcode 26.0 ... is installed, but Xcode 27.2 is required.` or a recommendation to `xcode-select -s` to the 27.2 install. |

To select Xcode 26.0 for S9 without changing the machine, first check whether `DEVELOPER_DIR` works:

```bash
DEVELOPER_DIR=<path of Xcode 26.0>/Contents/Developer xcode-select -p
```

If that prints the Xcode 26.0 path, prefix the S9 commands with the same `DEVELOPER_DIR=...`. If it doesn't, ask the person before running `sudo xcode-select -s <path of Xcode 26.0>`, and restore the selection recorded in step 1 afterwards.

## 4. Premise check (optional)

This confirms that a `net10.0-ios` build with Xcode 27.2 fails as #618 reports, which is what the check now predicts.

```bash
cd "$WORK"
dotnet new ios -o ios10
cd ios10
sed -i '' 's#<TargetFramework>net11.0-ios</TargetFramework>#<TargetFramework>net10.0-ios</TargetFramework>#' ios10.csproj
grep TargetFramework ios10.csproj
dotnet build
```

Expected: the build fails with `This version of .NET for iOS (26.0.11017) requires Xcode 26.0. The current version of Xcode is 27.2.` If the template doesn't create `net11.0-ios`, change whatever `TargetFramework` it creates to `net10.0-ios`.

## 5. Report

Fill in this table. For every row that doesn't match, paste the full uno-check output under it.

| # | Ran | Matches | Notes |
| --- | --- | --- | --- |
| S1 | | | |
| S2 | | | |
| S3 | | | |
| S4 | | | |
| S5 | | | |
| S6 | | | |
| S7 | | | |
| S8 | | | |
| S9 | | | |
| Premise | | | |

Include the machine details from step 1, the uno-check version, and the install option used (A or B). If the person asks you to post the report, add it as a comment on the branch's draft PR: `gh pr comment <number> -R unoplatform/uno.check --body-file <report file>`.

When done, restore the Xcode selection if it changed, and remove `$WORK`.
