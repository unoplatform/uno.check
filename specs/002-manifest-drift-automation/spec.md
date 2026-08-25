# uno-check: Manifest Drift Detection & Release-Pending Automation

## Overview & Objectives

The three manifests under `manifests/` (`uno.ui`, `uno.ui-preview`, `uno.ui-preview-major`)
are **embedded resources** in the `Uno.Check` NuGet package (`UnoCheck/UnoCheck.csproj`).
Two consequences drive this spec:

1. A manifest edit reaches nobody until a new package ships — merging to `main` only
   publishes a `-dev` prerelease; users on the stable tool keep the pins from the last
   `release/stable/X.Y` release.
2. The pinned versions track release trains that Uno does not control: the .NET SDK
   servicing schedule (Patch Tuesday), workload manifest packages (Android/iOS/tvOS/
   MacCatalyst/MAUI/wasm-tools, each on its own cadence), Xcode, and Microsoft OpenJDK.

Until now, noticing that these had moved — or that manifest commits were sitting on
`main` unreleased — was a matter of someone remembering to look. When this automation
first ran against live data it found the pinned Android workload three servicing
releases behind, iOS bindings a minor version behind, and **nine manifest commits that
had been unreleased for 157 days**.

### Key objective

A scheduled, zero-maintenance watchdog that:

- detects when any pinned version in any manifest channel has fallen behind its
  upstream source, or when a pinned download URL has died;
- detects when manifest changes on `main` are waiting on a stable release;
- surfaces everything in **one** self-maintaining GitHub issue (plus an optional
  webhook ping), with enough context that acting on it requires no archaeology;
- never auto-edits a manifest (see *Non-goals*).

### Non-goals

- **No automatic manifest updates / auto-PRs.** The authoritative source for workload
  pins is `dotnet workload list` on a clean machine (`AGENTS.md` §5), because package
  feeds contain builds that were never part of a shipped SDK. The automation reports
  drift; a human validates and transcribes the real versions.
  *Superseded by spec 003*: the generator there runs the clean-machine procedure on a
  runner instead of transcribing feeds; this detector stays signal-only.
- **No paging/alerting infrastructure.** A tracking issue and an optional webhook are
  the alarm surface. Anything more belongs to whoever consumes the webhook.
- **No tracking of tool-code (C#) changes pending release** — scope is the manifests.
  Extending the release-pending check beyond `manifests/` would fire on every docs
  commit.

---

## Components

| File | Role |
|------|------|
| `.github/scripts/Check-ManifestDrift.ps1` | Detector. Pure reporting — reads the manifests, queries upstream sources, emits a tiered findings report (JSON + Markdown + step summary). No side effects. |
| `.github/scripts/Sync-DriftIssue.ps1` | Reporter. Reconciles the findings report with a single labelled tracking issue via `gh`. Idempotent. |
| `.github/workflows/manifest-drift.yml` | Orchestration. Daily schedule + `workflow_dispatch` + PR validation runs. |
| `manifests/README.md` | Operator documentation (channels, release flow, check catalogue, local usage). |

### Detector checks

| Kind | Tier | Source | Meaning |
|------|------|--------|---------|
| `sdk-servicing` | Action | `builds.dotnet.microsoft.com` release metadata | A newer .NET SDK exists **within the pinned feature band** |
| `sdk-band-move` | Advisory | same | A newer feature band exists for the pinned channel |
| `workload` | Action | NuGet flat-container, per manifest `packageSources` | A newer workload manifest package is published |
| `workload-unknown-package` | Action | same | The `packageId` matches nothing on any configured feed, so that workload cannot be drift-checked |
| `url-dead` | Action | HTTP probe of every `urls.*` entry | A pinned download URL no longer resolves |
| `release-pending` | Advisory → Action after grace (default 7 days) | git tags vs `origin/main` | Manifest commits newer than the latest stable tag are not in any stable release |
| `tool-version` | Action | `version.json` vs `check.toolVersion` | The manifest demands a tool version no release can satisfy (or omits it, which aborts `--ci` runs) |
| `channel-overlap` | Advisory | manifest comparison | Preview pins are identical to stable — `--pre` offers users nothing |
| `xcode` | Advisory | `xcodereleases.com` | A newer Xcode GA exists (raising `minimumVersion` is a policy decision) |
| `openjdk` | Advisory | Adoptium API + `aka.ms` probe | A newer JDK of the pinned major exists **and** Microsoft has published its installer |
| `manifest-parse` | Advisory | local | A pinned value could not be parsed, so it is not being drift-checked |
| `source-offline` | Advisory | local | An upstream source was unreachable — the affected checks are unproven this run, not passing |

---

## Design decisions

### 1. Signal, not autofix

The "Available" column in a finding is a drift signal read from package feeds. Feeds
carry builds that never shipped in an SDK, and the `version/band` pairs in the manifest
are the .NET workload resolver's own output, not free-choice values. The report says so
explicitly and points at the `AGENTS.md` §5 clean-machine procedure. This mirrors how
the manifests have always been updated; the automation removes the *noticing* burden,
not the *validation* step.

### 2. Two tiers, so the alarm stays credible

Every finding is either **Action** (something in this repository must change) or
**Advisory** (a judgement call, or a degraded-confidence note). Only Action items feed
the issue-change detection and the webhook. Mixing "Android bindings are 3 releases
behind" with "a new Xcode exists somewhere" is how watchdogs get muted.

Two tier rules worth calling out:

- **In-band servicing is Action; a feature-band move is Advisory.** The tool's SDK check
  is `>=` (`DotNetCheckup.cs`), but moving bands rewrites every workload `packageId`
  suffix and the band half of every workload pin — a deliberate, verify-on-clean-machine
  decision.
- **Stable channels only chase nuget.org-visible versions.** A version that exists only
  on a `dnceng` build feed is downgraded to Advisory for stable manifests (possibly an
  unreleased build); preview channels keep it as Action, since previews legitimately
  consume the dotnet build feeds first.

### 3. One self-maintaining issue, state carried in-band

`Sync-DriftIssue.ps1` maintains a single issue labelled `manifest-drift`:

- no drift, no issue → no-op;
- drift, no issue → open one (label auto-created, optional assignees);
- drift, issue exists → **edit the body in place**; comment *only when the Action set
  actually changed*, listing what is new and what resolved;
- drift gone → comment and close.

The previous Action set rides inside the issue body as a base64-encoded HTML comment,
so change detection needs no external storage, survives runner recycling, and cannot
desync from what the issue displays. A daily schedule therefore produces zero
notifications on quiet days.

### 4. Feeds are resolved the way the tool resolves them

Workload packages are queried against **each manifest's own `packageSources`** (Azure
DevOps service index resolved to its flat-container base URL, nuget.org fallback), so
the checker sees what `uno-check --fix` would see. Which feed served each version is
recorded, feeding the stable-vs-build-feed tier rule above.

### 5. Fail-safe posture: a broken checker must not read as "no drift"

- The detector runs its **offline self-test suite (24 assertions** on SemVer2
  comparison, feature-band math, variable resolution, and safe property access**)
  before every real run** and refuses to report if it fails. The workflow also runs it
  as a dedicated step.
- Unreachable sources produce `source-offline` findings instead of silently passing.
- Manifest traversal uses StrictMode-safe accessors (`Get-Prop`), so a future manifest
  shape degrades to "nothing to check for this node" rather than crashing.
- The workflow also triggers on PRs touching `manifests/**` or the automation itself,
  so script regressions surface at review time, not at 07:00 UTC.

### 6. Release-pending accounting

`git log <latest-stable-tag>..origin/main -- manifests/` (tags matching `^\d+\.\d+(\.\d+)?$`,
version-sorted). Advisory for the first `-ReleaseGraceDays` (default 7) so normal
merge-then-release flow doesn't alarm; Action after that. Requires `fetch-depth: 0`.

Known edge: commits cherry-picked onto a `release/stable/*` branch keep different SHAs
and still count as pending. Accepted — manifest cherry-picks are rare, and the finding
text lists the commits so the situation is recognizable at a glance.

### 7. Advisory-only external trains

Xcode and OpenJDK minimums are policy (raising the Xcode floor before the matching
iOS/tvOS/MacCatalyst bindings are pinned would tell macOS users to upgrade for
workloads that don't exist). The OpenJDK check derives the major from each manifest's
`OPENJDK_VERSION` (no hardcoded "17"), and only reports a version whose Microsoft
installer actually exists (probed via the same `aka.ms` URL pattern the manifest uses).

---

## Included manifest fix: wasm-tools `packageId`

`uno.ui.manifest.json` and `uno.ui-preview.manifest.json` pinned wasm-tools to
`packageId: Microsoft.NET.Workload.Mono.ToolChain.Manifest-10.0.100`, which does not
exist on nuget.org or the dnceng dotnet10 feed (both 404). The real package is
`Microsoft.NET.Workload.Mono.ToolChain.Current.Manifest-10.0.100` (both 200);
`uno.ui-preview-major` already used the `.Current.` form, and the functionally-matched
field `workloadManifestId` (`microsoft.net.workload.mono.toolchain.current`) was already
correct in all three.

Impact assessment: in the tool, `PackageId` is only interpolated into the not-installed
diagnostic message (`UnoCheck/Checkups/DotNetWorkloadsCheckup.cs`), so installs were
never broken — but the wrong id made wasm-tools invisible to this drift checker (a
guaranteed `workload-unknown-package` finding). Fixed here (one line per manifest) so
the automation covers all seven workloads from day one; after the fix the checker
immediately reported real wasm drift (`10.0.105` → `10.0.110`), confirming coverage.

---

## Operations

### Configuration

| Setting | Where | Effect |
|---------|-------|--------|
| `MANIFEST_DRIFT_ASSIGNEES` | repository variable | Comma-separated logins assigned when the issue is first opened |
| `MANIFEST_DRIFT_WEBHOOK` | repository secret | Optional webhook (Discord-style JSON) pinged when Action items exist; absent = no-op |
| `-ReleaseGraceDays` | script parameter | Days manifest commits may sit unreleased before becoming Action (default 7) |

Workflow permissions: `contents: read`, `issues: write`. On `pull_request` events the
issue-sync and webhook steps are skipped (summary + artifact only), so fork PRs with
read-only tokens work.

### Local usage

```pwsh
# Offline unit tests only
./.github/scripts/Check-ManifestDrift.ps1 -SelfTest

# Full check (network; ~2 min). -SkipUrlCheck / -SkipAdvisory / -SkipGitCheck to narrow.
./.github/scripts/Check-ManifestDrift.ps1 -JsonOut drift.json -MarkdownOut drift.md

# Render the would-be issue body without touching GitHub
./.github/scripts/Sync-DriftIssue.ps1 -ReportPath drift.json -BodyOut body.md -DryRun
```

### First-run expectation

The first scheduled run will open the tracking issue with the currently-true drift
(at authoring time: 17 Action items — SDK `10.0.201`→`10.0.204` in-band, all six
platform workloads behind, .NET 11 preview-major one preview behind, and the
release-pending backlog since `1.34.1`).

---

## Validation

Evidence labels per the debugging-discipline convention:

- **Runtime** — detector self-test: 24/24 passing (offline, no network).
- **Runtime** — full detector run against live endpoints (release metadata, nuget.org,
  dnceng, xcodereleases, Adoptium, download URL probes): completes in ~2 min, findings
  spot-checked against manual `curl` queries of the same feeds; wasm-tools coverage
  verified before/after the `packageId` fix (`workload-unknown-package` → real
  `workload` finding).
- **Runtime** — `Sync-DriftIssue.ps1 -DryRun`: body renders, embedded state
  round-trips (17 keys decoded), simulated set-change diff labels added/removed
  correctly, multi-assignee binding produces `--assignee a --assignee b`.
- **Code review** — workflow YAML parsed; expression edge cases for `inputs.*` on
  non-dispatch events and empty/whitespace `MANIFEST_DRIFT_ASSIGNEES` exercised locally.
- **Not exercised** — live `gh issue` create/edit/close against the real repository
  (would post to the public tracker). First `workflow_dispatch` run shakes this out;
  the failure mode is a failed workflow step, not a wrong issue.

## Limitations & future work

- Feed-visible ≠ SDK-shipped: a workload version can appear on nuget.org hours before
  the SDK that resolves it. The clean-machine validation step absorbs this; the report
  warns against verbatim copying.
- `ci.yml`'s host SDK (`DOTNET_VERSION`) is not compared against the manifest pins; the
  two can drift independently. Candidate future check.
- Xcode data comes from a community-maintained source (`xcodereleases.com`); it is
  Advisory-only partly for that reason.
- The `release-pending` check reads tags; a repository cloned without tags reports a
  `source-offline` advisory rather than failing.
