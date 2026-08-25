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

For every drift category the detector reports, produce the highest safe level of
automation:

| Change | PR creation | Merge |
|---|---|---|
| In-band SDK servicing bump (`sdk-servicing`) | automatic | **auto-merge** on green |
| Workload pin bumps, same band (`workload`) | automatic | **auto-merge** on green |
| `xcode.minimumVersion` bump coupled to an iOS/tvOS/MacCatalyst pin | automatic, in the same PR | human approve |
| Feature-band move (`sdk-band-move` + policy triggers) | automatic | human approve |
| Stable release cut (`release-pending`) | n/a | one-click `workflow_dispatch` |

The end state: automation performs 100% of the *work* (detection, generation,
validation, PR hygiene, release mechanics). Humans retain exactly two decisions —
approving a band move, and pressing the release button.

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
  irreversible; `release-pending` nags, a human presses the button.
- **No auto-merge for band moves or Xcode floor raises**, ever — these change what the
  tool *demands* of user machines, which is policy. Automation prepares; a human approves.
- **No new E2E infrastructure.** `ci.yml` already runs `uno-check --ci --fix` per
  manifest on Windows/macOS/Linux runners; those jobs (plus the drift detector) are the
  merge gate. This spec adds required-check wiring, not new test frameworks.
- **No `check.toolVersion` bumps by the bot.** That field means "this manifest needs
  tool features version X"; only a human editing tool behavior knows that. The bot only
  *validates* it remains satisfiable (reusing 002's `tool-version` check).

---

## Components

| File | Role |
|------|------|
| `.github/scripts/New-ManifestUpdate.ps1` | Generator. Pure: given a channel + category (+ optional target band), computes the new pin set on the runner via the clean-machine procedure, rewrites the manifest JSON, emits a machine-readable change summary + PR body. No git/GitHub side effects. |
| `.github/scripts/Sync-UpdatePr.ps1` | PR reconciler. Maintains one branch + PR per category via `gh`: create, force-push refresh, retitle, enable/disable auto-merge, close when drift resolves. Idempotent, mirrors `Sync-DriftIssue.ps1`'s design. |
| `.github/workflows/manifest-update-pr.yml` | Orchestration. Runs after the daily drift run (`workflow_run` on `manifest-drift.yml`) + `workflow_dispatch`. Consumes `drift.json`, decides which category PRs to (re)generate, runs the generator on the OS matrix, invokes the reconciler. |
| `.github/workflows/cut-release.yml` | Release button. `workflow_dispatch` with a typed confirmation input; runs `nbgv prepare-release` to create `release/stable/{version}` (per `version.json`), push it (triggering the existing `publish_prod`), and bump `main` to the next `-dev` version. |
| `Check-ManifestDrift.ps1` (existing) | Unchanged as detector. Gains one output field per finding: the open bot-PR URL when one exists, so the tracking issue says "PR pending" instead of raising a raw Action (see decision 8). |
| `manifests/README.md` | Operator docs: category table, kill switch, approval expectations, band policy. |

---

## Design decisions

### 1. Generation = the clean-machine procedure, executed by a runner

For a target channel and SDK version, the generator:

1. Installs the exact target .NET SDK into a **private directory**
   (`DOTNET_INSTALL_DIR` under the workspace, `dotnet-install` script, and a
   `global.json` with `"rollForward": "disable"`) so the runner's preinstalled SDKs
   cannot leak into resolution — the same reason `AGENTS.md` §5 says "custom folder".
2. Runs `dotnet workload update --print-rollback` against the manifest's own
   `packageSources` (matching how both `uno-check --fix` and 002's detector resolve
   feeds).
3. Transcribes the rollback output **verbatim** — the `version/band` pairs are the .NET
   workload resolver's own output, never free-choice values — into `check.variables`,
   and sets `DOTNET_SDK_VERSION` to the SDK it actually installed.
4. Enforces the invariant `packageId band suffix == band half of the pin` for every
   workload entry (this class of mismatch is exactly the wasm-tools `packageId` bug 002
   found and fixed by hand; here it becomes structurally impossible).
5. Emits `update.json`: old→new per variable, the trigger that motivated the run, feed
   provenance per version (nuget.org vs build feed), and per-step confidence flags
   consumed by the auto-merge decision (decision 4).

The generator runs on **each OS in the matrix** and the orchestrator diffs the results:
platform-conditional workloads (`supportedPlatforms`) mean no single OS sees every pin.
Disagreement between OSes on a *shared* pin is a hard stop (no PR, Action finding).

### 2. The merge gate is the tool itself, on the machines we ship to

The existing `ci.yml` jobs already do the strongest possible validation: build the tool
from the branch, then run `uno-check --ci --fix --non-interactive` per manifest on
clean `windows-latest` / `macos-latest` / `ubuntu-latest` runners — a fresh machine
taken to green by the exact bits under review, including real workload installs via the
rollback-file path (`DotNetWorkloadManager`). A same-band pin bump that passes this on
all three OSes is *better* validated than the historical manual process.

Additions this spec makes to the gate:

- Run `Check-ManifestDrift.ps1` against the PR's manifests and require the findings the
  PR claims to resolve to actually clear (self-consistency: the bot may not open a PR
  that doesn't fix what it says it fixes).
- Mark the per-OS `uno-check` jobs + drift re-check as **required status checks** on
  `main` branch protection.
- The E2E jobs run with `--skip xcode` (runner Xcode inventory varies), so the gate
  does **not** validate `xcode.minimumVersion`. Consequence: any PR that touches the
  Xcode floor is excluded from auto-merge by construction — see decision 5.

### 3. One self-refreshing PR per category, never a PR-per-day stream

Fixed branch names, one PR each, force-push regeneration on every drift run —
mirroring the tracking issue's edit-in-place model:

| Branch | Contents |
|---|---|
| `bot/manifest-servicing` | `DOTNET_SDK_VERSION` in-band bumps + same-band workload pins (+ coupled Xcode floor when required) |
| `bot/manifest-band-move` | Full band move: SDK, every workload pin regenerated under the new band, `packageId` suffixes, Xcode floor |
| (per channel where needed) | Preview/preview-major variants reuse the same two categories with a channel suffix, e.g. `bot/manifest-servicing-preview` |

Rules: while a band-move PR is open, the servicing PR for the same channel is closed
and suppressed (the band move regenerates everything; two open PRs editing the same
pins is churn). PR bodies carry the previous change-set as an embedded state comment
(base64 HTML comment, same trick as the drift issue) so "did anything change since the
human last looked" is answerable without external storage; a refresh that changes the
Action content dismisses stale approvals (GitHub does this on force-push) — correct
behavior, since the human approved different pins.

### 4. Auto-merge is a per-PR decision the bot makes conservatively

Mechanics: repository setting *Allow auto-merge* + branch protection required checks;
the reconciler runs `gh pr merge --auto --squash` only when **all** hold:

- category is `servicing` (never `band-move`);
- the diff touches only `manifests/*.manifest.json` (no schema drift, no stray files);
- no generator confidence flag is degraded (no `source-offline` during generation, no
  cross-OS pin disagreement, no build-feed-only version on a **stable** channel —
  preview channels may consume build feeds, matching 002's tier rule);
- `xcode.minimumVersion` is unchanged;
- the kill switch (`MANIFEST_AUTOPR_MODE`, see *Operations*) is set to `auto-merge`.

Anything else leaves a normal PR awaiting review. Auto-merge failure is soft: the PR
simply stays open and the drift issue keeps nagging.

### 5. Xcode floor: extracted fail-closed, human-approved always

When a generated pin set moves Microsoft.iOS/tvOS/MacCatalyst to a new version, the
generator locates the dotnet/macios GitHub release for that version (tag/name match on
the binding version) and parses the formulaic requirement line
(`Xcode <version> is required with this release`). Outcomes:

- **Parse succeeds, floor unchanged** → servicing PR, auto-merge eligible.
- **Parse succeeds, floor must rise** → same PR includes the `xcode.minimumVersion`
  bump *and* is demoted to human-approve (decision 4 excludes it). Raising the floor
  before users can obtain that Xcode is a policy call; and the E2E gate can't test it
  (`--skip xcode`).
- **Parse fails / release not found** → PR opens as draft with the iOS-family pins
  held back at their current values and a warning in the body. Fail closed: never ship
  a binding bump whose Xcode requirement is unknown, and never guess the floor.

The macios *minimum SDK* line (`requires .NET SDK <version>`) is parsed the same way
and feeds band-move trigger 2.

### 6. Band-move PRs are generated, not just suggested

Trigger detection is mechanical, extending 002's `sdk-band-move` advisory:

- **Trigger 1 (stale band)**: newest `Microsoft.NET.Workloads.<pinned-band>` package is
  older than `STALE_BAND_DAYS` (default 45) **and** a successor band has published sets.
- **Trigger 2 (Xcode support requires newer band)**: the newest iOS-family workload
  release's parsed minimum SDK exceeds the pinned band.

On either trigger the orchestrator runs the generator with the **latest stable band**
as target and opens/refreshes `bot/manifest-band-move` with the trigger named in the
body. The PR is complete (SDK, all pins, packageIds, Xcode floor, per-OS validated) —
the human contribution is reading and approving. Trigger 3 (Uno.Sdk baseline) has no
automated signal yet; a `workflow_dispatch` input lets a human request a band-move PR
generation on demand.

### 7. Release cut: one click, never zero clicks

`cut-release.yml` (`workflow_dispatch`, input `confirm` must equal `release`):

1. `nbgv prepare-release` on `main` → creates `release/stable/{version}` per
   `version.json`'s `release.branchName`, bumps `main` to the next `-dev` version.
2. Pushes both; the existing `publish_prod` job (trigger: `release/**` push) signs,
   publishes to nuget.org, and tags.

002's `release-pending` check keeps nagging (Action after 7 days), now pointing at the
button instead of at tribal knowledge. Deliberately not automated further — see
*Non-goals*.

### 8. The drift issue stays the single source of truth

The tracking issue must not double-alarm for drift that already has a PR waiting, and
must not go quiet about it either. Findings with an open bot PR render as
`Action (PR pending: #123)`; they count as "unchanged" for comment-triggering purposes
unless the PR's content changed. Findings whose PR sits unapproved for
`PR_STALE_DAYS` (default 7) escalate back to plain Action — a stalled approval is
drift.

### 9. Bot identity: GitHub App, not `GITHUB_TOKEN`

PRs created with the workflow's default `GITHUB_TOKEN` do not trigger other workflows —
the E2E gate would never run and required checks would hold every bot PR forever. The
reconciler authenticates as a dedicated GitHub App (id + private key in secrets;
fallback: a fine-grained PAT in `MANIFEST_BOT_TOKEN`). The App needs `contents: write`,
`pull_requests: write`; auto-merge additionally requires the repo's *Allow auto-merge*
setting and branch protection listing the gate jobs as required.

---

## Operations

### Configuration

| Setting | Where | Effect |
|---|---|---|
| `MANIFEST_AUTOPR_MODE` | repository variable | `off` (no PRs) / `pr-only` (PRs, never auto-merge) / `auto-merge` (full). Default `pr-only`. The kill switch. |
| `MANIFEST_BOT_APP_ID` / `MANIFEST_BOT_APP_KEY` | secrets | GitHub App identity for PR creation (or `MANIFEST_BOT_TOKEN` PAT fallback) |
| `MANIFEST_PR_REVIEWERS` | repository variable | Comma-separated logins requested on human-approve PRs |
| `STALE_BAND_DAYS` | script parameter | Band-move trigger 1 threshold (default 45) |
| `PR_STALE_DAYS` | script parameter | Days before an unapproved bot PR re-escalates in the drift issue (default 7) |

### Repository prerequisites (one-time)

1. Branch protection on `main`: require the per-OS `uno-check` E2E jobs + drift
   re-check; enable *Allow auto-merge*; dismiss stale approvals on push.
2. Install the GitHub App / provision the PAT.
3. `CODEOWNERS` entry for `manifests/` so human-approve PRs auto-request the right people.

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
| 1 | `MANIFEST_AUTOPR_MODE=pr-only`; every PR human-merged | 3 consecutive servicing PRs merged with zero manual edits |
| 2 | `auto-merge` for `sdk-servicing`-only diffs (workload pins still human-merged) | 3 clean auto-merges |
| 3 | `auto-merge` for all servicing-category PRs | steady state |
| — | Band moves and Xcode floors: human-approve forever | n/a |

---

## Validation (planned evidence)

- **Runtime** — generator offline self-test: transcription fidelity (`version/band`
  verbatim), `packageId`/band invariant, cross-OS merge logic, macios release-notes
  parsing against recorded fixtures (including a deliberately unparseable body → held-back
  draft behavior).
- **Runtime** — full generation on all three OS runners against the live 10.0.2xx →
  10.0.4xx state; resulting manifest taken to green by the existing `ci.yml` E2E matrix.
- **Runtime** — `Sync-UpdatePr.ps1 -DryRun`: branch naming, body state round-trip,
  auto-merge eligibility matrix (each guard in decision 4 exercised true/false).
- **Runtime** — `cut-release.yml` against a fork: branch created, `main` version bumped,
  `publish_prod` triggered (publish step stubbed).
- **Not exercised pre-merge** — live App-authenticated PR creation and real auto-merge
  on this repository; first `workflow_dispatch` run in `pr-only` mode shakes this out
  (failure mode: a failed workflow step, not a wrong merge).

## Limitations & future work

- **macOS runner Xcode inventory** lags Apple by weeks; even a future non-skipped Xcode
  E2E check could not validate a floor the image doesn't carry. Xcode floors stay
  human-approved regardless.
- **Linux runners cannot validate iOS/MacCatalyst installs** (`supportedPlatforms`);
  each OS validates its subset and the union must cover every workload — the
  orchestrator fails generation if any workload is validated by no OS.
- **macios release-notes parsing is prose parsing.** Formulaic today; the fail-closed
  path (decision 5) is the mitigation, and a machine-readable upstream source (e.g.
  `WorkloadManifest.json` metadata) should replace it if one appears.
- **Band-move trigger 3 (Uno.Sdk baseline)** needs a signal from Uno CI — candidate:
  `repository_dispatch` from unoplatform/uno when its tested SDK band changes.
- **`ci.yml` host SDK (`DOTNET_VERSION`)** still drifts independently of the manifests
  (carried over from 002); the generator could bump it in the same PR once the E2E jobs
  prove insensitive to it.
- **Cherry-picks to `release/stable/*`** remain invisible to `release-pending`
  (inherited from 002).
