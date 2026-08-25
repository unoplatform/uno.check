# uno-check: Automated Manifest Update PRs (Auto-PR & Auto-Merge)

## Overview & Objectives

Spec `002-manifest-drift-automation` gave us the *noticing*: a daily detector and a
self-maintaining tracking issue. Acting on a finding is still fully manual — clean VM,
`dotnet workload list`, transcribe, open a PR, remember to cut a release. This spec
automates the acting.

It deliberately **supersedes 002's first non-goal** ("No automatic manifest updates /
auto-PRs"). That non-goal existed because package feeds are a drift *signal*, not a
validated set — feeds carry builds that never shipped in any SDK, and per-package
"latest" versions are not coherent with each other. The objection is not that
automation can't be trusted; it's that *feed transcription* can't be trusted. This
spec's generator therefore does not transcribe feeds. It automates the documented
clean-machine procedure itself (`AGENTS.md` §5): a fresh GitHub-hosted runner **is** a
clean machine, and `dotnet workload update --print-rollback` under the pinned SDK **is**
the authoritative, coherent version set. What was the human's validation step becomes
the generator's construction step.

### Key objective

For every finding kind the detector reports, produce the highest safe level of
automation. "Category" below is the name of the bot branch a kind maps to; "merge rule"
is what decision 4 enforces.

| Detector kind (002) | Category | PR creation | Merge |
|---|---|---|---|
| `sdk-servicing` | `servicing` | automatic | **auto-merge** on green |
| `workload`, same workload band (decision 3 definition) | `servicing` | automatic | **auto-merge** on green |
| `workload`, band half or `packageId` suffix changes | `band-move` | automatic | human approve |
| `sdk-band-move` + policy triggers (decision 6) | `band-move` | automatic | human approve |
| `xcode` floor rise coupled to an iOS/tvOS/MacCatalyst pin (decision 5) | joins whichever PR carries the pin | automatic, same PR | human approve |
| `release-pending` | n/a | n/a | one-click `workflow_dispatch` (decision 7) |
| `url-dead`, `workload-unknown-package`, `tool-version`, `channel-overlap`, `openjdk`, `xcode` (uncoupled), `manifest-parse`, `source-offline` | none | issue-only, unchanged from 002 | n/a |

Per channel:

| Channel | Servicing PR | Band-move PR | Band-move target |
|---|---|---|---|
| `default` (stable) | `bot/manifest-servicing` | `bot/manifest-band-move` | latest **stable** band of the pinned major |
| `preview` | `bot/manifest-servicing-preview` | `bot/manifest-band-move-preview` | latest band of the pinned major, prerelease allowed |
| `preview-major` | `bot/manifest-servicing-preview-major` | `bot/manifest-band-move-preview-major` | latest band of the *next* major, prerelease allowed; there is no stable band to target |
| `main` | none — `main`'s manifest is what the other three become after merge | | |

The end state: automation performs 100% of the *work* (detection, generation,
validation, PR hygiene, release mechanics). Humans retain exactly three decisions —
approving a band move, approving an Xcode floor raise, and pressing the release button.

### The band policy being automated

The stable manifest does **not** chase every new feature band immediately; it moves on
triggers. Encoded here so the automation and the humans agree on the rules:

1. **Move when the pinned band goes stale**: no new workload set for the pinned band
   while a successor band has them (the 10.0.2xx band received its last set ~1 week
   after 10.0.3xx shipped; non-1xx bands die fast).
2. **Move when a needed Xcode-support release requires a newer band**: dotnet/macios
   Xcode-support releases declare a minimum SDK (e.g. Xcode 26.5 support required
   10.0.302+). Apple's App Store submission rules force a current Xcode yearly, so this
   trigger is inevitable, not optional.
3. **Move when Uno.Sdk / templates bump their tested baseline** (human-initiated;
   see *Limitations*).

When moving: go to the **latest** stable band (intermediate bands die as soon as their
successor ships), and land the move in the preview channel ahead of stable when both
need it.

### Non-goals

- **No unattended stable releases.** Publishing to nuget.org is outward-facing and
  irreversible; `release-pending` nags, a human presses the button. Note the exception
  this does *not* cover: released binaries run with `--main` read `main`'s manifest live
  (`UnoCheck/ToolInfo.cs`, `manifests/README.md` "Main"). An auto-merged servicing bump
  therefore reaches `--main` users immediately. That is what `--main` is for (the dev
  channel), and it is accepted here — the E2E gate has run the new manifest on all
  three OSes before it lands. The `--main` channel is not a stable release and is
  documented as such.
- **No auto-merge for band moves or Xcode floor raises**, ever — these change what the
  tool *demands* of user machines, which is policy. Automation prepares; a human approves.
- **No new E2E infrastructure.** `ci.yml` already runs `uno-check --ci --fix` per
  manifest on Windows/macOS/Linux runners; those jobs (plus the drift detector) are the
  merge gate. This spec adds required-check wiring and one host-SDK fix (decision 2),
  not new test frameworks.
- **No `check.toolVersion` bumps by the bot.** That field means "this manifest needs
  tool features version X"; only a human editing tool behavior knows that. The bot only
  *validates* it remains satisfiable (reusing 002's `tool-version` check).
- **No Dependabot/Renovate.** Neither understands the `version/band` pair or the
  packageId-suffix invariant, and both transcribe feeds — the exact thing 002 rejected.

### Changes to spec 002

| 002 item | Disposition |
|---|---|
| Non-goal 1 "No automatic manifest updates / auto-PRs" | **Retired.** Replaced by this spec; the reasoning survives in decision 1 (generate from the resolver, never from feeds). |
| Decision 1 "Signal, not autofix" | **Amended.** The detector remains signal-only. The *generator* (a separate script) produces the fix from the workload resolver, not from the detector's "Available" column. |
| `Check-ManifestDrift.ps1` output shape | **Unchanged.** No new fields; "PR pending" is rendered by the reporter (decision 8). |
| `Sync-DriftIssue.ps1` state key `Kind\|Manifest\|Subject\|Current\|Latest` | **Unchanged.** PR URL is render-only and never joins the key. |
| `manifests/README.md` "verify on a clean machine" / "never copy feed versions verbatim" | **Amended** to describe the generator as the clean-machine step (in scope for this spec, see *Components*). |
| `AGENTS.md` §5 | **Amended** to point at the generator for the mechanical steps; the human procedure stays as the fallback (in scope). |
| Everything else (kinds, tiers, tracking issue, webhook, PR validation run) | **Unchanged.** |

A one-line "superseded by 003" note is added to 002's non-goal 1.

---

## Components

| File | Role |
|------|------|
| `.github/scripts/ManifestDrift.Common.psm1` | **New, extracted from `Check-ManifestDrift.ps1`.** `Read-CheckManifest`, `Resolve-ManifestVariable`, `Get-FlatContainerBase`, `Get-FeatureBand`, `Split-WorkloadVersion`, the flat-container existence probe. The detector and the generator consume the same code; neither re-implements band math. |
| `.github/scripts/New-ManifestUpdate.ps1` | Generator. Given a channel + category (+ optional target band), installs the target SDK into a private directory, runs the workload resolver, and applies **value-only text edits** to the manifest (decision 1). Emits `update.json` (change summary, provenance, confidence flags) + PR body. No git/GitHub side effects. Runs feed-downloaded code, so it holds **no secrets**. |
| `.github/scripts/Sync-UpdatePr.ps1` | PR reconciler. Maintains one branch + PR per category via `gh`: create, refresh only on content change, retitle, arm/disarm auto-merge, close when drift is *proven* resolved. Idempotent, mirrors `Sync-DriftIssue.ps1`'s design including its unproven-run hold. The only place the bot credential exists. |
| `.github/workflows/manifest-drift.yml` (existing) | Gains two jobs after `report`: `generate` (unprivileged) and `reconcile` (bot credential), both under the existing `github.event_name != 'pull_request'` guard. **No new workflow, no `workflow_run`** — see decision 10. |
| `.github/workflows/ci.yml` (existing) | Gains a fan-in `e2e-gate` job and installs the manifest's pinned SDK instead of a fixed host SDK (decision 2). |
| `.github/workflows/cut-release.yml` | Release button. `workflow_dispatch` under the `Production` environment; runs `nbgv prepare-release`, pushes `release/stable/{version}` + the `main` bump atomically (decision 7). |
| `Check-ManifestDrift.ps1` (existing) | **Unchanged.** Stays pure and unprivileged; it runs on PR-authored code. |
| `Sync-DriftIssue.ps1` (existing) | Gains "PR pending" rendering: reads `gh pr list --head bot/manifest-*` and decorates matching findings (decision 8). |
| `manifests/README.md`, `AGENTS.md` §5, `specs/002/spec.md` | Doc edits listed under *Changes to spec 002*. |

---

## Design decisions

### 1. Generation = the clean-machine procedure, executed by a runner

For a target channel and SDK version, the generator:

1. Installs the exact target .NET SDK into a **private directory**
   (`DOTNET_INSTALL_DIR` under the workspace, `dotnet-install` script, and a
   `global.json` with `"rollForward": "disable"`) so the runner's preinstalled SDKs
   cannot leak into resolution — the same reason `AGENTS.md` §5 says "custom folder".
2. Runs `dotnet workload update --print-rollback`. `--print-rollback` is a hidden
   (undocumented) option of the SDK; the exact invocation is pinned in the script and
   covered by the self-test so an SDK that drops it fails loudly rather than silently
   producing an empty set. Feeds: **stable channels resolve against nuget.org only**;
   preview channels resolve against the manifest's own `packageSources` (matching 002's
   tier rule). This replaces §5's `workload install` + `workload list` pair — same
   resolver, same answer, no multi-GB install.
3. Transcribes the rollback output **verbatim** — the `version/band` pairs are the .NET
   workload resolver's own output, never free-choice values — into `check.variables`,
   and sets `DOTNET_SDK_VERSION` to the SDK it actually installed. Only variables that
   already exist in `check.variables` are updated; rollback entries with no
   corresponding variable (e.g. `emscripten.*`, `macos`) are listed in `update.json` as
   informational and never added.
4. Enforces the invariant `packageId band suffix == band half of the pin` for every
   workload entry (this class of mismatch is exactly the wasm-tools `packageId` bug 002
   found and fixed by hand; here it becomes structurally impossible).
5. **Edits the manifest as text, value-only.** No JSON round-trip: the generator
   replaces the string value of each affected `check.variables` entry, `packageId`
   suffix, and `xcode.minimumVersion` in place, preserving key order, indentation,
   inline-array formatting and line endings. Guard: the number of changed lines in the
   diff must equal the number of entries in `update.json`; anything else is a generator
   bug and aborts. This keeps human-approve PRs eyeballable, keeps the file shape stable
   for older clients (`Manifest.cs` deserializes with `TypeNameHandling.Auto`; the bot
   never introduces a key), and makes the cross-run byte comparison in decision 3
   meaningful.
6. Emits `update.json`: old→new per variable, the trigger that motivated the run, feed
   provenance per version, and per-step confidence flags consumed by the auto-merge
   decision (decision 4). Provenance on a stable channel is verified by a nuget.org
   flat-container existence probe (the detector's existing check, via the shared
   module) — rollback output carries no feed origin, so this is the only way to prove
   "not build-feed-only".

The generator runs **once, on `ubuntu-latest`**. `--print-rollback` reads the
SDK-bundled workload manifests, which ship for every workload on every OS; only the
*packs* are platform-conditional. Per-OS validation is the E2E matrix's job (decision
2), not the generator's.

Outbound calls (dotnet-install, the resolver's feed access, the macios release lookup)
use the detector's timeout/retry settings (`TimeoutSec` + `MaximumRetryCount 2`); the
`generate` job carries `timeout-minutes: 45`. A timeout sets the `source-offline`
confidence flag rather than failing the job.

### 2. The merge gate is the tool itself, on the machines we ship to

The existing `ci.yml` jobs already do the strongest possible validation: build the tool
from the branch, then run `uno-check --ci --fix --non-interactive` per manifest on
clean `windows-latest` / `macos-14`+`macos-15` / `ubuntu-latest` runners — a fresh
machine taken to green by the bits under review, including real workload installs via
the rollback-file path (`DotNetWorkloadManager`).

**Today that gate does not exercise the SDK pin.** `ci.yml` preinstalls a fixed host
SDK (`DOTNET_VERSION`, currently `10.0.302`); the tool's SDK check passes on `>=`
(`DotNetCheckup`), then exports the *newest* SDK it finds and installs workloads under
that one. A manifest pinning a non-existent `10.0.207` goes green on all 45 jobs. So
this spec makes the following changes to the gate — they are prerequisites for phase 2,
not future work:

- The matrix's "Install base .NET SDK" step installs the **manifest under test's**
  `DOTNET_SDK_VERSION` (read from the manifest file) into a private
  `DOTNET_INSTALL_DIR` with `rollForward: disable`, so the tool's SDK check and the
  workload installs run against the pinned SDK. The `Upgrade` jobs keep the fixed host
  SDK: they exist to validate upgrading an old tool build, not the new manifest.
- `Check-ManifestDrift.ps1` already runs on PRs that touch `manifests/**`
  (`manifest-drift.yml` `pull_request` trigger). The bot's PR body lists the finding
  keys it claims to resolve; the PR run asserts those keys are absent from its own
  `drift.json` (self-consistency: the bot may not open a PR that doesn't fix what it
  says it fixes). PR runs use a per-ref concurrency group so six bot PRs don't queue
  behind each other and the scheduled run.
- A `manifest-update-policy` step in the same PR run fails when the head branch is
  `bot/manifest-servicing*` and the diff changes any band half, `packageId` suffix,
  `xcode.minimumVersion`, or any file outside `manifests/*.manifest.json`. A
  servicing branch can only merge with a servicing-shaped diff, regardless of what the
  reconciler armed.
- A fan-in job `e2e-gate` (`needs: [build_tool, run_tests, testwin, testmac, testlinux]`,
  `if: always()`, fails unless every dependency succeeded) is the **only** E2E name
  branch protection references. The matrix expands to ~50 job names that change
  whenever a manifest is added; requiring them individually would silently unblock or
  permanently block PRs on every rename.
- The E2E jobs run with `--skip xcode` (runner Xcode inventory varies), so the gate
  does **not** validate `xcode.minimumVersion`. Consequence: any PR that touches the
  Xcode floor is excluded from auto-merge by construction — see decision 5.

### 3. One self-refreshing PR per category, never a PR-per-day stream

Fixed branch names (table under *Key objective*), one PR each, mirroring the tracking
issue's edit-in-place model.

**"Same band" is defined on the workload band, not the SDK band.** The SDK feature
band (`10.0.2xx`) and the workload manifest band (`version/band` = `10.0.100`,
`packageId` suffix `-10.0.100`) are different things; the stable manifest today pins
SDK `10.0.201` with workloads on `/10.0.100`. A servicing change leaves every
`version/band` band half and every `packageId` unchanged and moves only the version
halves and `DOTNET_SDK_VERSION` within its band. Anything else — including
`11.0.100-preview.7` → `11.0.100` on `preview-major`, which `Get-FeatureBand` maps to
one band — is a band move.

**Refresh only on content change.** Each run regenerates into a temporary tree and
compares it byte-for-byte with the branch head. Identical → no push, no new SHA, no CI
run, approvals intact. Different → force-push with a deterministic commit (fixed
author, commit date = manifest content hash, not wall clock) so re-running the same
input never produces a new SHA. GitHub dismisses stale approvals on that push — correct
behavior, since the human approved different pins.

While a band-move PR is open, the servicing PR for the same channel is closed and
suppressed (the band move regenerates everything; two open PRs editing the same pins is
churn). PR bodies carry the previous change-set as an embedded state comment (base64
HTML comment, same trick as the drift issue) so "did anything change since the human
last looked" is answerable without external storage.

**Close only when proven resolved.** The reconciler closes a PR only when the drift run
that fed it completed with no degraded checks and reports none of the PR's finding keys
— the same rule `Sync-DriftIssue.ps1` applies before closing the issue. A
`source-offline` day leaves PRs untouched.

### 4. Auto-merge is a per-PR decision the bot makes conservatively

Mechanics: repository setting *Allow auto-merge* + the branch rulesets in *Repository
prerequisites*; the reconciler runs `gh pr merge --auto --squash` only when **all**
hold:

- category is `servicing` (never `band-move`);
- the diff touches only `manifests/*.manifest.json` (no schema drift, no stray files);
- no generator confidence flag is degraded (no `source-offline` during generation, no
  build-feed-only version on a **stable** channel — preview channels may consume build
  feeds, matching 002's tier rule);
- `xcode.minimumVersion` is unchanged;
- no `manifest-hold` label on the PR and no `manifest-hold` entry matching a changed
  version (decision 11);
- the kill switch (`MANIFEST_AUTOPR_MODE`, see *Operations*) is set to `auto-merge`;
- **the ruleset is in place**: the reconciler reads the active rulesets for `main` and
  refuses to arm unless `e2e-gate`, `Check manifest drift` and
  `manifest-update-policy` are required checks. `gh pr merge --auto` on an unprotected
  branch merges immediately; the bot must never rely on a one-time manual step having
  been done.

Anything else leaves a normal PR awaiting review. Auto-merge failure is soft: the PR
simply stays open and the drift issue keeps nagging. Arming is re-evaluated on every
run, and `off` / `pr-only` run `gh pr merge --disable-auto` on every open bot PR
immediately — the kill switch takes effect on the next run, not the next merge.

On a successful auto-merge the reconciler comments on the tracking issue and pings
`MANIFEST_DRIFT_WEBHOOK` (when set) with the merged PR link. Silence is not success.

### 5. Xcode floor: extracted fail-closed, human-approved always

When a generated pin set moves Microsoft.iOS/tvOS/MacCatalyst to a new version, the
generator locates the dotnet/macios GitHub release for that version (authenticated via
`GH_TOKEN`; one list call per run, cached; match on the binding version appearing in
the release tag — actual tag shapes are recorded as fixtures during validation, since
the naming is `dotnet-<band>-xcode<ver>-…`, not the bare binding version) and parses
the formulaic requirement line (`Xcode <version> is required with this release`).
Outcomes:

- **Parse succeeds, floor unchanged** → servicing PR, auto-merge eligible.
- **Parse succeeds, floor must rise** → same PR includes the `xcode.minimumVersion`
  bump *and* is demoted to human-approve (decision 4 excludes it). Raising the floor
  before users can obtain that Xcode is a policy call; and the E2E gate can't test it
  (`--skip xcode`).
- **Parse fails / release not found** → the PR opens as a **draft with the full
  coherent pin set** and a warning naming the unresolved floor. The generator never
  hand-mixes held-back iOS pins with new pins (that is the incoherent set decision 1
  exists to prevent); a human resolves the floor and marks the PR ready. Fail closed:
  never ship a binding bump whose Xcode requirement is unknown, and never guess the
  floor.

The macios *minimum SDK* line (`requires .NET SDK <version>`) is parsed the same way
and feeds band-move trigger 2.

### 6. Band-move PRs are generated, not just suggested

Trigger detection is mechanical, extending 002's `sdk-band-move` advisory:

- **Trigger 1 (stale band)**: newest `Microsoft.NET.Workloads.<pinned-band>` package is
  older than `STALE_BAND_DAYS` **and** a successor band has published sets. Both facts
  are read from `drift.json` (the detector already fetched them); the generator does
  not re-query NuGet. Default 45 days: workload sets ship on the monthly servicing
  cadence, so 45 days is "one full cycle missed, plus slack" — long enough to survive a
  skipped month, short enough that a dead band (typically ~1 week after its successor)
  is caught on the next cycle.
- **Trigger 2 (Xcode support requires newer band)**: the newest iOS-family workload
  release's parsed minimum SDK exceeds the pinned band.

On either trigger the orchestrator runs the generator with the channel's target band
(table under *Key objective*) and opens/refreshes the channel's band-move PR with the
trigger named in the body. The PR is complete (SDK, all pins, packageIds, Xcode floor)
— the human contribution is reading and approving. Trigger 3 (Uno.Sdk baseline) has no
automated signal yet; a `workflow_dispatch` input lets a human request a band-move PR
generation on demand.

### 7. Release cut: one click, never zero clicks

`cut-release.yml` (`workflow_dispatch`, input `confirm` must equal `release`) runs
under the existing `Production` GitHub environment, which already gates `publish_prod`
with required reviewers. `confirm` prevents accidents; the environment provides
authorization — anyone with write can *dispatch*, only an environment reviewer can
*run* it.

1. Takes the `manifest-bot` concurrency group (shared with the reconciler, decision 10)
   so no bot push can land between the next two steps.
2. `nbgv prepare-release` on `main` → creates `release/stable/{version}` per
   `version.json`'s `release.branchName`, bumps `main` to the next `-dev` version.
3. `git push --atomic origin release/stable/{version} main`: both refs or neither. The
   existing `publish_prod` job (trigger: `release/**` push) signs, publishes to
   nuget.org, and tags.

Credentials: `GITHUB_TOKEN` cannot be used (a push with it does not trigger
`publish_prod`), and the reconciler's bot credential is deliberately not shared.
`RELEASE_BOT_TOKEN` is an **environment secret** of `Production` (fine-grained PAT or a
second App installation) listed as a bypass actor on the `main` rulesets for the version
bump only. Recovery for a failed atomic push is "nothing happened, re-run"; a release
branch that already exists (previous run pushed, then failed after) is reported and the
run stops — never re-cut over it.

002's `release-pending` check keeps nagging (Action after 7 days), now pointing at the
button instead of at tribal knowledge. Deliberately not automated further — see
*Non-goals*.

### 8. The drift issue stays the single source of truth

The tracking issue must not double-alarm for drift that already has a PR waiting, and
must not go quiet about it either. `Sync-DriftIssue.ps1` (which already holds
`GH_TOKEN`) lists open `bot/manifest-*` PRs and renders matching findings as
`Action (PR pending: #123)`. The detector is untouched — it stays pure and runs on
fork-PR code without a token. The PR URL is render-only: the state key is unchanged, so
"PR pending" findings count as "unchanged" for comment-triggering purposes unless the
PR's content changed (detected via the PR body's state comment). Findings whose PR sits
unapproved for `PR_STALE_DAYS` (default 7) escalate back to plain Action — a stalled
approval is drift.

A failed `generate` or `reconcile` job is surfaced the same way: the `reconcile` job
runs `if: always()` and, when its own or the generator's step failed, appends a
`generator-failed` Action line to the tracking issue and pings the webhook. A red
Actions tab nobody looks at is not a signal.

### 9. Bot identity: GitHub App, not `GITHUB_TOKEN`

PRs created with the workflow's default `GITHUB_TOKEN` do not trigger other workflows —
the E2E gate would never run and required checks would hold every bot PR forever. The
reconciler authenticates as a dedicated GitHub App (id + private key in secrets;
fallback: a fine-grained PAT in `MANIFEST_BOT_TOKEN`). The App needs `contents: write`,
`pull_requests: write`.

The installation token is minted **inside the `reconcile` job only** and never reaches
the `generate` job, which executes SDK-downloaded workload/MSBuild code. `generate`
checks out with `persist-credentials: false` and has no secrets in scope — the same
job-scoped model `manifest-drift.yml` already uses for the reporter.

The App is a bypass actor for the *approval* ruleset only, never for required status
checks (see *Repository prerequisites*). Branch protection cannot express "approval
required only for band moves", so enforcement is layered: the reconciler arms only
servicing branches, `manifest-update-policy` fails a servicing branch whose diff is not
servicing-shaped, and required checks have no bypass at all.

### 10. Trigger boundary: jobs in `manifest-drift.yml`, not `workflow_run`

`manifest-drift.yml` runs on `pull_request` for `manifests/**` — every bot PR touches
that path, and fork PRs can rewrite `Check-ManifestDrift.ps1`. A separate workflow
chained with `workflow_run` would fire for those runs too, which means (a) a
privileged job consuming a fork-authored `drift.json` — the exact hole 002 closed by
job-scoping secrets — and (b) an unbounded loop: bot push → drift PR run →
`workflow_run` → regenerate → push, cancelling in-flight CI each cycle.

So there is no orchestration workflow. `generate` and `reconcile` are jobs appended to
`manifest-drift.yml`, `needs: report`, under the existing
`github.event_name != 'pull_request'` guard, consuming the same `drift.json` artifact
the reporter already uses. They cannot be reached from a PR. `workflow_dispatch` gains
inputs for category / channel / target band so a human can request a specific PR.

The `reconcile` job takes the `manifest-bot` concurrency group (`cancel-in-progress:
false`), shared with `cut-release.yml`.

### 11. Rollback: hold first, revert second

An auto-merged manifest is live for `--main` users at once and in the next `-dev`
package minutes later (`publish_dev` on `main` push). A plain revert PR does not work —
the next run regenerates the same pins and re-merges them. So:

1. **Stop the bleeding:** add the `manifest-hold` label to the open servicing PR, or
   add the bad version to `manifests/manifest-hold.json` (`{ "variable": "...",
   "version": "...", "reason": "...", "until": "yyyy-mm-dd" }`). The generator skips
   held versions (keeps the current pin) and the reconciler never arms a PR whose diff
   contains one. The detector reports the held drift as Advisory, not Action, so the
   issue doesn't nag about a decision already taken.
2. **Revert:** a normal revert PR on `main`; the hold keeps the bot from undoing it.
3. **Re-cut** if a stable release already shipped the bad pin.

Instant global stop, independent of the daily run: disable the repository's *Allow
auto-merge* setting — GitHub cancels every pending auto-merge at once.

---

## Operations

### Configuration

| Setting | Where | Effect |
|---|---|---|
| `MANIFEST_AUTOPR_MODE` | repository variable | `off` (no pushes; open PRs stay open but auto-merge is disarmed) / `pr-only` (PRs, never auto-merge) / `auto-merge` (full). Default `pr-only`. Unrecognised values behave as `off`. |
| `MANIFEST_BOT_APP_ID` / `MANIFEST_BOT_APP_KEY` | secrets | GitHub App identity for PR creation (or `MANIFEST_BOT_TOKEN` PAT fallback). Read by the `reconcile` job only. |
| `RELEASE_BOT_TOKEN` | `Production` environment secret | Push credential for `cut-release.yml`; bypass actor for the `main` version bump. |
| `MANIFEST_PR_REVIEWERS` | repository variable | Comma-separated logins requested on human-approve PRs |
| `MANIFEST_DRIFT_WEBHOOK` | secret (existing) | Also pinged on auto-merge and on generator failure |
| `STALE_BAND_DAYS` | script parameter | Band-move trigger 1 threshold (default 45, rationale in decision 6) |
| `PR_STALE_DAYS` | script parameter | Days before an unapproved bot PR re-escalates in the drift issue (default 7) |
| `manifests/manifest-hold.json` | file | Versions the bot must not propose (decision 11) |

### Repository prerequisites (one-time)

1. Two rulesets on `main` (two, because bypass is per-ruleset):
   - **checks**: required status checks `e2e-gate`, `Check manifest drift`,
     `manifest-update-policy`; require branches up to date; **no bypass actors**.
   - **review**: require a pull request, 1 approval, dismiss stale approvals on push,
     require review from Code Owners; bypass actors: the manifest bot App (servicing
     auto-merge) and `RELEASE_BOT_TOKEN`'s identity (version bump).
2. Enable *Allow auto-merge*.
3. Install the GitHub App / provision the PATs; add `RELEASE_BOT_TOKEN` to the
   `Production` environment.
4. `CODEOWNERS` entry for `manifests/` so human-approve PRs auto-request the right people.
5. Pin the mutable action tags in `.github/workflows/actions/tag-release` (`@v2`,
   `@v1`) to SHAs — they are now on the release path.

### Local usage

```pwsh
# Generate a same-band update for the stable channel into a working copy (no side effects)
./.github/scripts/New-ManifestUpdate.ps1 -Channel default -Category servicing -OutDir ./out

# Generate a band move to a specific band
./.github/scripts/New-ManifestUpdate.ps1 -Channel default -Category band-move -TargetBand 10.0.400 -OutDir ./out

# Render what the reconciler would do, without touching GitHub
./.github/scripts/Sync-UpdatePr.ps1 -UpdatePath ./out/update.json -DryRun
```

### Rollout plan

| Phase | State | Exit criterion |
|---|---|---|
| 1 | `MANIFEST_AUTOPR_MODE=pr-only`; every PR human-merged; decision 2 gate changes landed | 3 consecutive servicing PRs merged with zero manual edits **and** no refresh push between open and merge (proves the no-op guard) |
| 2 | `auto-merge` for all servicing-category PRs | steady state; "clean" = no `manifest-hold`, no revert, no regression finding within 7 days of merge |
| — | Band moves and Xcode floors: human-approve forever | n/a |

(An intermediate "SDK-only auto-merge" phase was considered and dropped: servicing PRs
carry SDK and workload bumps together, so an SDK-only PR rarely exists, and it would
have needed a fourth mode value.)

---

## Validation (planned evidence)

Acceptance scenarios — each is an observable outcome, not an implementation check:

| Scenario | Expected outcome |
|---|---|
| Servicing bump, all green, mode `auto-merge` | merged without human action; tracking issue comment + webhook ping |
| Servicing bump, one OS red in `e2e-gate` | PR stays open; not merged |
| Servicing bump with a non-existent SDK version | `e2e-gate` red on every OS (SDK install fails under `rollForward: disable`); not merged |
| Servicing branch whose diff changes a `packageId` suffix | `manifest-update-policy` red; not merged even if armed |
| `xcode.minimumVersion` changed, mode `auto-merge` | PR open, not armed, reviewers requested |
| macios release-notes unparseable | draft PR with the full pin set and a warning; never a partial set |
| Same drift two days running | second run pushes nothing; PR SHA unchanged; approvals intact |
| `source-offline` during generation | PR neither refreshed nor closed; issue held |
| Version listed in `manifest-hold.json` | generator keeps current pin; PR not armed; drift rendered Advisory |
| Mode flipped to `off` with an armed PR open | auto-merge disarmed on the next run; PR stays open |
| Rulesets missing `e2e-gate` | reconciler refuses to arm and reports why |
| Fork PR editing `Check-ManifestDrift.ps1` | `generate` / `reconcile` do not run; no secret in any job that ran |
| `cut-release` with a concurrent bot push | bot push waits on `manifest-bot`; both release refs land or neither |
| `cut-release` when `release/stable/{version}` exists | run stops, nothing pushed |

Evidence per component:

- **Runtime** — shared module + generator offline self-test: transcription fidelity
  (`version/band` verbatim), `packageId`/band invariant, value-only edit guard
  (changed lines == `update.json` entries; a manifest with CRLF stays CRLF), macios
  release-tag and release-notes parsing against recorded fixtures (real tag names; a
  deliberately unparseable body → draft-with-full-set behavior).
- **Runtime** — full generation on `ubuntu-latest` against the live 10.0.2xx →
  10.0.4xx state; resulting manifest taken to green by the `ci.yml` matrix with the
  pinned-SDK install in place.
- **Runtime** — `Sync-UpdatePr.ps1 -DryRun`: every row of the acceptance table that
  the reconciler decides, exercised true/false.
- **Runtime** — `cut-release.yml` against a fork: branch created, `main` version bumped,
  `publish_prod` triggered (publish step stubbed), existing-branch refusal.
- **Not exercised pre-merge** — live App-authenticated PR creation and real auto-merge
  on this repository; first `workflow_dispatch` run in `pr-only` mode shakes this out
  (failure mode: a failed workflow step, not a wrong merge).

## Limitations & future work

- **macOS runner Xcode inventory** lags Apple by weeks; even a future non-skipped Xcode
  E2E check could not validate a floor the image doesn't carry. Xcode floors stay
  human-approved regardless.
- **Linux runners cannot validate iOS/MacCatalyst installs** (`supportedPlatforms`);
  each OS in the E2E matrix validates its subset, and the union must cover every
  workload — `e2e-gate` fails if any workload in the manifest is validated by no OS.
- **Old clients vs new manifest.** The `Upgrade` E2E jobs run the previous tool build
  without `--manifest`, so "released binary N-1 reads the new `main` manifest" is not
  exercised. The value-only edit rule (decision 1.5) is the mitigation: the bot never
  changes the file's shape, only version strings.
- **macios release-notes parsing is prose parsing.** Formulaic today; the fail-closed
  path (decision 5) is the mitigation, and a machine-readable upstream source (e.g.
  `WorkloadManifest.json` metadata) should replace it if one appears.
- **Band-move trigger 3 (Uno.Sdk baseline)** needs a signal from Uno CI — candidate:
  `repository_dispatch` from unoplatform/uno when its tested SDK band changes.
- **Upstream → user latency** is dominated by the 7-day `release-pending` grace, not by
  CI. A shorter grace for bot-merged commits is a possible follow-up.
- **Cherry-picks to `release/stable/*`** remain invisible to `release-pending`
  (inherited from 002).
