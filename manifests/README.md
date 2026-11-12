# Manifest Files

These are used by the tool to parse up-to-date versions and information on which things to validate and install.

They are **embedded resources** in the `Uno.Check` package (see `UnoCheck/UnoCheck.csproj`), so a manifest
change does not reach users until a new package is published to NuGet.

They are grouped into the following channels:

### Default (stable)
Used when no channel flag is passed. Should align with the current stable .NET SDK and workloads:
- `uno.ui.manifest.json`

### Preview
Used with `--pre` / `--preview`. Tracks in-band previews of the current .NET major:
- `uno.ui-preview.manifest.json`

### Preview major
Used with `--pre-major` / `--preview-major`. Tracks previews of the *next* .NET major:
- `uno.ui-preview-major.manifest.json`

Only Uno Platform 7 and later support the next .NET major, and Uno 7 removed the Mac Catalyst head, so this
manifest installs no `maccatalyst` workload, and it uses `maui-android` and `maui-ios` instead of `maui`, because
`maui` also installs Mac Catalyst. Today it otherwise pins the same .NET 11 SDK and workload versions as the default
and preview manifests. Those keep `maccatalyst` and `maui`, since they also serve Uno Platform 6 projects with a
Mac Catalyst head. uno-check doesn't uninstall workloads, so a machine set up by an earlier pre-major manifest
keeps `maui` and `maccatalyst`.
The .NET 11 Android workload requires JDK 21 or later, and the .NET 11 SDK requires Visual Studio (MSBuild) 18.7
or later. `ManifestVersionGuardTests` enforces these two rules for every .NET 11+ manifest, and the Mac Catalyst
rule for this one. Uno Platform 7.0 is stable, so this manifest installs the latest stable Uno Platform templates and
Uno.Sdk, like the others. Set `unoPackages.includePrerelease` only while a manifest's .NET SDK needs an unreleased Uno major.

### Main
Used with `--main` / `--dev-manifest`. This is the only channel that is **not** embedded: it is fetched at
runtime from `main`, so changes take effect without a tool release.
- https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui.manifest.json

## Updating the Manifest

To update the manifest, the usual process is:

1. Use a clean VM and install the latest .NET version.
2. Run:
   ```bash
   dotnet workload install ios android maccatalyst tvos maui wasm-tools wasm-tools-net10
   ```
   For `uno.ui-preview-major.manifest.json` (Uno Platform 7), install `ios android tvos maui-android maui-ios wasm-tools wasm-tools-net10` instead.

   On a .NET 11 or later SDK, `wasm-tools` builds only that SDK's TFM. Uno templates default to `net10.0`,
   which needs `wasm-tools-net10`, or the WebAssembly build fails with UNOWA0001. `ManifestVersionGuardTests`
   checks that every .NET 11+ manifest installs it, with the same version as `wasm-tools`. With `--tfm`, uno-check
   installs a `wasm-tools-net<N>` workload only when a WebAssembly target framework uses .NET `<N>` (issue #621).
3. Run:
   ```bash
   dotnet workload list
   ```
4. Use the output to update the manifest with the correct versions.
5. Update the `xcode` section from the pinned Apple workloads. Each Apple SDK pack accepts one Xcode major.minor, its
   `_RecommendedXcodeVersion` (in the pack's `targets/Microsoft.iOS.Sdk.Versions.props`, or `Versions.plist`).
   The iOS workload's `WorkloadManifest.json` lists a pack per target framework, for example
   `Microsoft.iOS.Sdk.net11.0_27.2` and `Microsoft.iOS.Sdk.net10.0_26.0`.
   - `minimumVersion` is the Xcode for the SDK's own target framework (`27.2` for iOS 27.2.11039).
   - `targetFrameworks` maps each older target framework to the Xcode its pack requires: `"net10.0": "26.0"` for the
     .NET 11.0.100 workloads, whose net10.0 packs are 26.0.11017. A `--tfm net10.0-ios` run checks that Xcode
     instead (issue #618). `ManifestVersionGuardTests` checks that every .NET 11+ manifest sets the `net10.0` entry.

Transcribe the **Manifest Version** column verbatim, including the `version/band` form
(for example `36.1.43/10.0.100`). Values live in the `check.variables` block; the workload
entries reference them as `$(ANDROID_SDK_VERSION)` and friends.

See `AGENTS.md` section 5 for the full procedure.

## After updating: cut a release

Merging a manifest change to `main` publishes a `-dev` prerelease only. Users keep getting the versions
from the last stable release until a `release/stable/X.Y` branch is pushed, which triggers the production
publish and tags the release.

## Automated drift detection

`.github/workflows/manifest-drift.yml` runs daily and reports when these manifests have fallen behind
upstream, or when manifest changes are waiting on a release.

| Check | Tier | Meaning |
| --- | --- | --- |
| `sdk-servicing` | Action | A newer .NET SDK exists in the pinned feature band |
| `sdk-band-move` | Advisory | A newer feature band exists; moving is a deliberate call |
| `workload` | Action | A newer workload manifest package is published |
| `workload-unknown-package` | Action | The `packageId` matches nothing on the configured feeds, so that workload is not being drift-checked |
| `url-dead` | Action | A download URL in the manifest no longer resolves |
| `release-pending` | Action after 7 days | Manifest commits on `main` are not in a stable release yet |
| `tool-version` | Action | `check.toolVersion` requires a tool newer than `version.json` |
| `channel-overlap` | Advisory | The preview manifest pins the same versions as stable |
| `xcode`, `openjdk` | Advisory | Newer releases exist; raising the minimum is a policy decision |
| `manifest-parse` | Advisory | A pinned version could not be parsed, so it is not being drift-checked |
| `source-offline` | Advisory | A data source was unreachable, so its checks are unproven this run |

Findings are collected into **one** tracking issue labelled `manifest-drift`. It is refreshed in place on
every run, comments only when the set of action items actually changes, and closes itself once the drift
is gone.

**The reported "Available" versions are a signal, not a validated set.** Always confirm on a clean machine
with `dotnet workload list` before editing a manifest — the checker reads package feeds, which include
builds that were never part of a shipped SDK.

### Running it locally

```pwsh
# Offline unit tests for the version/band comparison helpers
./.github/scripts/Check-ManifestDrift.ps1 -SelfTest

# Full check (network); add -SkipUrlCheck for a faster run
./.github/scripts/Check-ManifestDrift.ps1 -JsonOut drift.json -MarkdownOut drift.md

# Render the tracking issue body without touching GitHub
./.github/scripts/Sync-DriftIssue.ps1 -ReportPath drift.json -BodyOut body.md -DryRun
```

### Configuration

| Setting | Where | Effect |
| --- | --- | --- |
| `MANIFEST_DRIFT_ASSIGNEES` | repository variable | Comma-separated logins assigned to a newly opened issue |
| `MANIFEST_DRIFT_WEBHOOK` | repository secret | Optional Discord-style webhook pinged when action items exist |
| `-ReleaseGraceDays` | script parameter | Days a manifest change may sit unreleased before it becomes an Action (default 7) |
