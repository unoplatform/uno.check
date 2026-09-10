# Reducing elevation prompts and unnecessary runs

Status: proposed — phased; Phase 0 is a prerequisite for the rest

## Problem

uno-check interrupts developers in two ways, and both come down to the same question: *what
can actually have changed since last time?*

**Prompts.** Fixing a fresh Windows machine through `uno-check --fix` can raise up to **eight**
separate UAC prompts, because Windows elevates whole processes rather than individual commands:
a host (or the CLI) must decide before launching, and each fix that touches a protected
location is its own elevated launch.

**Time.** Hosts that run uno-check eagerly — in the background at startup, to show whether the
environment needs attention — pay a full run every launch. Measured on the reference machine
(appendix A) that is **7.4s** for a desktop-only run and **11.4s** for desktop + Android, on
every single launch, mostly to re-confirm machine state that cannot have changed.

`fix.requires_elevation` (spec 003) told hosts *which* fixes need elevation, which stopped the
needless prompts. It did not reduce the number of fixes that genuinely need it, and it says
nothing about when a check is worth running at all. This spec covers both.

### The distribution matters more than the count

Of the eight fixes that really need administrator rights today, **three write only the .NET
root** — and those three are the only ones a developer hits *repeatedly*:

| Fix | Writes | How often it re-fires |
|---|---|---|
| `dotnet` (SDK install) | `%ProgramFiles%\dotnet` + HKLM bundle registration | every pinned-SDK bump — **16 times in 24 months** |
| `dotnetworkloads-<v>` | `<SdkRoot>\{packs,sdk-manifests,metadata}` | every workload bump, tracking the SDK band |
| `dotnettargetingpacks` | same subtrees, via `dotnet workload update` | on drift |
| `openjdk` | `%ProgramFiles%\Microsoft\jdk-*` (MSI) | manifest-pinned — twice in 24 months |
| `androidsdk` | `%ProgramFiles(x86)%\Android\android-sdk` | manifest-pinned; elevation is location-dependent |
| `windowslongpath` | `HKLM\SYSTEM\...\FileSystem\LongPathsEnabled` | once per machine, permanently |
| `windowshyperv` | `dism /Online /Enable-Feature` | once per machine, permanently |
| `git` | launches the VS Installer (self-elevating) | once per machine |

Frequencies are counted from the git history of `manifests/` on `main`, Sep 2024 → Sep 2026,
not estimated. Note that **`openjdk` is not a "once per machine, never again" item** even though
it feels like one: its floor is pinned by the manifest and moved twice in that window
(17.0.16 → 17.0.19). Any design that hardcodes a never-recheck list will silently stop
noticing that class of bump. See "Recurrence classification" below.

So the goal is not "zero prompts ever" — enabling Hyper-V or a long-path registry key is a
machine configuration change and will always require consent. The goal is that **the everyday
path costs nothing, the one-time machine setup costs one prompt once, and a run that cannot
discover anything new does not happen at all.**

### Recurrence classification

This table is the shared foundation for both halves of the spec: it decides which fixes recur
(the elevation work, phases 0-3) and which checks are worth re-running (the caching work,
phase 4). Every row was traced to the source that decides it.

| Class | Depends on | Checkups | Consequence |
|---|---|---|---|
| **Machine state** | nothing versioned; only what the user did to the machine | `windowshyperv`, `windowslongpath`, `git`, `psexecpolicy`, `vswin` | Cannot change because Uno shipped something. Cacheable until explicitly re-run or a max age elapses. |
| **Manifest-pinned** | `manifests/uno.ui.manifest.json` | `openjdk`, `dotnet`, `dotnetworkloads-<v>`, `dotnettargetingpacks`, `unosdk`, `dotnetnewunotemplates`, `androidsdk`, `androidemulator` | The only class that can go from green to red without the user touching anything. Must re-run when the manifest moves. |
| **Ambient** | process/environment state at this instant | `vsrestart` (is VS running now), `dotnetroots` (env drift), `https-dev-cert` (expiry), `edgewebview2` | Not cacheable in any useful sense, but individually cheap. |

## Goals

1. **Zero prompts on the recurring path**: SDK, workloads, targeting packs, templates, Uno SDK,
   Android SDK/emulator, dev-certs, PowerShell policy.
2. **At most one prompt for one-time machine configuration**, and only when the user explicitly
   asks for those fixes.
3. **Never report healthy for a toolchain the user's IDE or CLI does not actually use.** This
   is the hard invariant; everything below is subordinate to it.
4. **A host can decide whether a run is worth doing in under 100ms**, without duplicating
   uno-check's own manifest and channel resolution.
5. **Never show a stale green as if it were fresh.** A cached result must carry when it was
   taken and against what, so a host can display it honestly.

## Non-goals

- **Taking a dependency on `dotnetup`.** The equivalent capability is already in-tree:
  `Solutions/DotNetSdkScriptInstallSolution.cs` runs `dotnet-install` with an explicit
  `-InstallDir`, and `DotNetSdk` already treats `DOTNET_ROOT` as the highest-precedence input.
  Adding a preview-quality external toolchain manager would buy nothing we cannot do directly,
  and would add a moving dependency to a tool people run on fresh machines. Revisit only if
  `dotnetup` becomes the platform default for SDK acquisition.
- **Making a managed root the single source of truth.** A host that also *drives the builds*
  can do this safely. uno-check validates a toolchain other tools use, so a private root it
  alone believes in is precisely how it would start reporting green on a broken machine.
- **Automating the Visual Studio Installer.** `vswinworkloads` stays advice-only.
- **A resident helper, daemon or background service.** Rejected on memory and footprint grounds
  by host-side product direction, and made unnecessary by phase 4: the expensive part is
  deciding *whether* to run, and that decision costs one conditional HTTP GET.
- **uno-check owning the cache.** The CLI stays stateless — it has no user-scoped store today
  and should not grow one. It exposes the identity a host needs to build a cache key (P4.1);
  the host owns storage, expiry and policy.
- **Self-elevating the whole tool so nothing prompts again.** This is what the shipped
  `uno-check.exe` does via its `requireAdministrator` manifest, and it is why the tool cannot
  start unelevated at all — even `--version` fails with Win32 error 740. Hosts that need
  unelevated diagnosis must keep invoking through the dotnet host.

## Current state

Relevant facts established by inspection (file references are to `dev/vs/json-output`):

- **The root is already a single choke point.** `DotNet/DotNetSdk.cs:59-85` resolves in order:
  `DOTNET_ROOT` from shared state (seeded by `--dotnet <SDK_ROOT>` or the ambient variable at
  `CheckCommand.cs:171-178`) → the vendored `EnvironmentProvider` (PATH) → well-known folders.
  The result is written back to shared state and injected into every child process.
- **Two of the three .NET prompts are already conditional.** `DotNetWorkloadsCheckup.cs:311-313`
  and `Solutions/DotNetWorkloadUpdateSolution.cs:26-32` compute elevation as
  `!Util.IsDirectoryWritable(<root>)`. Point the root somewhere user-writable and they stop
  prompting **with no further code change**.
- **The third is one branch.** `Checkups/DotNetCheckup.cs:99-113` routes interactive Windows to
  `MsInstallerSolution` (the machine-wide `.exe`, unconditionally elevated) and only uses the
  in-tree script installer under `Util.CI || Util.IsLinux`.
- **The divergence risk is already known.** `Checkups/DotNetRootsCheckup.cs:64-71` exists to
  warn when the effective root and the PATH root differ (issue #542). Any user-local design
  makes that checkup load-bearing rather than advisory.

## Design

### Invariant

> uno-check validates the root the machine actually resolves. It may *choose* a user-local root
> only if it also *persists* that choice, so the IDE and the terminal resolve the same one.

A user-local root that uno-check knows about and nothing else does is a worse outcome than a
UAC prompt.

### Phase 0 — correctness prerequisites (blocking)

These are bugs today; they become false-green generators the moment a non-default root is in
play. Phase 1 must not ship without them.

| # | Item | Why it blocks |
|---|---|---|
| P0.1 | Route the four bare-`dotnet` call sites through the resolved muxer: `Checkups/DotNetNewUnoTemplatesCheckup.cs:70` (raw `ProcessStartInfo` — does not even inherit the injected `DOTNET_ROOT`), `Solutions/DotNetNewTemplatesInstallSolution.cs:43,49,53`, `Solutions/UnoSdkSolution.cs:60`, `Checkups/HttpsDevCertCheckup.cs:31,50` | With a user root that is not first on `PATH`, these silently examine and modify a different SDK than the one being validated. This is the false-green scenario, concretely. |
| P0.2 | `DotNetSdk.cs:50-54` probes `$HOME/share/dotnet`; the real convention is `$HOME/.dotnet` | A user-local install is invisible to well-known-folder resolution. |
| P0.3 | Align arch-variable precedence: `DotNetSdk` reads only `DOTNET_ROOT`, while `DotNetRootsCheckup.cs:81-94` and `DotNetTargetingPackAlignmentCheckup.cs:109-116` prefer `DOTNET_ROOT_<ARCH>` | A root designated only by the arch variable produces an internally inconsistent run: some checkups examine root A while workloads install into root B. |
| P0.4 | `Checkups/WindowsLongPathCheckup.cs:20` opens HKLM **writable** merely to read | Unelevated it fails before it can report state, surfacing as "Requested registry access is not allowed" with no fix offered. Read-only probe, write only in the solution. |
| P0.5 | `Solutions/PythonIsInstalledSolution.cs:19-24` only opens a Store URL, but inherits `RequiresElevation => true` | A prompt for nothing — and elevating a browser launch tends to break it. Mirror `LinuxNinjaOpenUrlSolution`. |
| P0.6 | Remove dead solutions: `CreateFileSolution` (zero references), `LinuxOtherDistGitCliSolution` (zero references) | Both would misreport elevation if revived. |
| P0.7 | `Solutions/GitSolution.cs` inherits `RequiresElevation => true`, but only starts the Visual Studio Installer, which self-elevates | A host wraps a self-elevating installer in its own elevated child, so the user consents twice for one action. Should be `false`. |

**P0.5 and P0.7 are no longer cosmetic.** A host that batches fixes partitions them by
`requires_elevation` and runs the machine-scoped group in one elevated child. A checkup
mis-reporting `true` therefore does not merely add a prompt — it joins the elevated batch, so
its work runs as administrator. For `windowspyhtonInstallation` that means launching the user's
browser elevated. Both are one-line fixes and should land before any host ships batching.

P0.4 and P0.5 are confirmed against a live unelevated run on the reference machine: `windowslongpath`
returns `status: "error"`, `"Requested registry access is not allowed."` and emits **no fix object at
all**, so the one-time registry fix is currently unreachable without an already-elevated tool.

### Phase 1 — user-local .NET root as a first-class scope

**P1.1 — an explicit scope switch.** New option `--dotnet-install-scope <auto|user|machine>`
(default `auto`). Surfaced in the structured contract so hosts can offer it.

**P1.2 — let Windows use the script installer.** Widen the `Util.CI || Util.IsLinux` gate at
`DotNetCheckup.cs:99` so that under `user` scope every platform uses
`DotNetSdkScriptInstallSolution`. Its elevation answer is already probe-based
(`DotNetSdkScriptInstallSolution.cs:31`), so it reports correctly for whichever root it targets.

**P1.3 — a user root default.** `DotNetSdkScriptInstallSolution.DefaultSdkRoot()` returns
Program Files on Windows (`:43-45`). Under `user` scope it must return the per-user location
(`%USERPROFILE%\.dotnet`, matching `dotnet-install`'s own default).

**P1.4 — persist the choice.** This is the part that makes it safe, and the part MAUI.Sherpa
sidesteps by owning the builds it validates. When uno-check installs into a user-local root it
must set, at **user** scope (never machine):
- `DOTNET_ROOT`
- `PATH` entry for that root, ordered ahead of the machine install

`Checkups/OpenJdkCheckup.cs:99-106` already does the equivalent for `JAVA_HOME`, and is the
precedent to follow — except at `EnvironmentVariableTarget.User`, which itself needs no
elevation. Add a checkup that verifies the persisted variables still point at the root
uno-check manages, so drift is reported rather than silently tolerated.

**P1.5 — the `auto` policy.** Choose `user` when *all* hold:
- no writable machine-wide SDK is already in use, **and**
- no Visual Studio installation is detected (`VisualStudioWindowsCheckup.GetWindowsInfo()`),
  since VS resolves its own SDK and a user root would diverge from what VS builds with

Otherwise `machine`. Rationale: the developers who benefit most (Rider, VS Code, CLI, CI) get
the prompt-free path; VS users keep the toolchain VS actually uses. `--dotnet-install-scope`
lets either group override.

**P1.6 — promote `DotNetRootsCheckup`.** Once a user root is a supported mode, a divergence
between the effective root and the PATH root stops being informational. Report it as an error
when uno-check itself created the user root, with a fix that repairs the persisted variables.

### Phase 2 — the remaining one-time prompts

**P2.1 — OpenJDK without an MSI.** Microsoft OpenJDK ships a `.zip`/`.tar.gz` alongside the
installer. Extracting to a per-user location and setting `JAVA_HOME` at user scope removes a
prompt. Requires a manifest addition (archive URLs beside the existing `urls`) and changes
`OpenJdkCheckup.cs:99-106` from a machine-scope `JAVA_HOME` write to a user-scope one.

**P2.2 — Android SDK per-user default.** `androidsdk` is already probe-based; it prompts only
because the Windows default is `%ProgramFiles(x86)%\Android\android-sdk`. When no
`ANDROID_HOME` exists, prefer a per-user location and persist it the same way as P1.4.

**P2.3 — one prompt for genuine machine settings.** `windowslongpath` and `windowshyperv`
cannot avoid elevation. They can share one. The `--only` contract already supports naming
several ids in one child (spec 003, host flow), so a host — or `--fix` itself — can apply all
selected machine-scoped fixes in a **single** elevated invocation:
`--fix --only windowslongpath --only windowshyperv`. To make that groupable without hosts
hardcoding a list, add an `elevation_scope` discriminator to `FixInfo`
(`"none" | "user" | "machine"`), so a host can partition the selected fixes into "run
unelevated now" and "one elevated batch".

**P2.4 — `git` needs no elevation of ours.** It launches the VS Installer, which self-elevates,
so there is nothing for uno-check to improve about the installation itself. What must change is
the classification: see P0.7.

### Phase 3 — host-side contract

Everything the CLI must expose so a host can be both quiet and honest:

| Field | Status | What a host does with it |
|---|---|---|
| `fix.requires_elevation` | shipped (spec 003) | Decides whether the fix child is launched elevated at all |
| `fix.elevation_scope` | P2.3 | Partition selected fixes into "run unelevated now" and "one elevated batch"; shield the affected rows before the user clicks |
| `manifest_version` / `manifest_etag` | P4.1 | Build a cache key and run the freshness gate without reimplementing manifest resolution |
| install scope in use | P1.1 | Say which .NET root the result describes — it changes what "your environment" means |

A host that installs into a user root must say so, and a host that shows a cached result must
say when it was taken.

### Phase 4 — background runs without redundant work

Host-side product direction is to run uno-check **in the background rather than behind a manual
button**, and to badge the navigation entry when something needs fixing. That is the right call
— an environment doctor nobody clicks is an environment doctor that never runs — but run
eagerly on every launch it costs 7.4–11.4s of CPU and network each time, largely re-confirming
things that cannot have changed.

The measurements (appendix A) point at a specific design rather than a vague "cache it":

**P4.1 — expose manifest identity in the contract (the only CLI change here).** `run_started`
and `report` carry `schema_version`, `tool_version`, `channel`, `targets` and `checkup_count`,
but **nothing identifying the manifest**. A host therefore cannot tell whether the pinned
versions moved. Add to both:

```
"manifest_version": "<the manifest's own version field>",
"manifest_etag":    "<ETag of the resolved manifest, when fetched over HTTP>"
```

Without this a host must fetch and parse the manifest itself, duplicating channel selection,
`--manifest` overrides and the embedded-fallback path in `ToolInfo.LoadManifest` — logic that
will drift from the CLI's. The CLI already resolves all of it; it should just say what it used.

**P4.2 — the freshness gate.** With P4.1 a host can decide whether to run at all with one
conditional GET against the manifest URL: measured at **424ms** for a full fetch (6.5 KB) and
**18ms** for a `304 Not Modified` on a cold connection, versus 7.4s for the cheapest useful
run. The gate costs well under 1% of what it can avoid.

**P4.3 — cache key.** A stored report is reusable while *all* of these are unchanged:
manifest identity (P4.1), `tool_version`, `channel`, and the `targets` set. Any difference
invalidates. In addition, invalidate on: a successful fix (re-verify that checkup immediately —
never let a fix be reported from cache), an explicit user re-run, and a **max age** so a machine
that changed underneath us is eventually rechecked anyway.

**P4.4 — what to re-run, and as one process.** When the gate says something *could* have
changed, the subset to re-run follows the recurrence classification: manifest moved → the
manifest-pinned class; otherwise → whatever was not green last time, plus the ambient class.

This must be **a single invocation naming several ids**, never one invocation per checkup.
Process startup is a fixed tax on every invocation — 0.85s through `dnx`, 0.20s for an
installed global tool — so a scoped run of one checkup costs about as much as a scoped run of
several. Splitting a subset across processes pays that tax once per checkup and can cost more
than the full run it was meant to avoid.

**P4.5 — two `--only` behaviours a host must build around.** Both were found by measurement:

- **The workloads id embeds the SDK version.** Caller-supplied ids match exactly, so
  `--only dotnetworkloads` matches nothing; the real id is `dotnetworkloads-10.0.201` and it
  changes with the SDK band. This fails safely — the run exits 1 with
  `"reason": "unknown checkup id(s) for --only: dotnetworkloads"` rather than silently passing —
  but it fails the *whole* batch. Hosts must take ids from `list --json` or a prior report and
  never construct them, and should re-resolve them if the SDK band may have moved since.
- **`--only` pulls in dependencies.** `--only dotnetworkloads-10.0.201` returns **5** checkup
  results, not 1, and takes 6.95s. Only the caller-named ids are fixed
  (`CheckCommand.IsCallerNamedForFix`), but a host must be ready for results it did not ask for.

**P4.6 — startup sequence.** The badge must not wait on any of this:

1. Load the persisted report and paint the badge immediately — zero work, zero network.
2. Run the freshness gate (P4.2) plus the cheap local comparisons (tool version, channel, targets).
3. Nothing changed and the last run was all green → **do not run**.
4. Nothing changed but issues were outstanding → re-run just those, one process (P4.4).
5. Manifest moved → re-run the manifest-pinned class.
6. Tool version, channel or targets changed, or max age exceeded → full run.
7. Always show *when* the displayed result was taken (goal 5).

**P4.7 — what the badge can say.** `requires_elevation` is already per-fix, so a host can
distinguish "3 issues, 2 fixable without a prompt" from "1 issue needing admin" before the user
clicks anything — worth using, since the two deserve different urgency.

### Phase 5 — verification

Each phase lands with unit coverage for the pure decisions (scope selection, root resolution,
elevation classification) plus a manual matrix, because the failure modes are environmental:

| Environment | What must be true |
|---|---|
| Windows + Visual Studio | `auto` picks `machine`; behaviour unchanged from today |
| Windows, no VS, no SDK | `auto` picks `user`; a full fix run completes with **zero** prompts except an explicitly requested machine-settings batch |
| Windows, existing machine SDK | `auto` picks `machine`; no silent switch |
| macOS | script installer into `~/.dotnet`; authorization dialog only for protected commands |
| Linux | unchanged (already user-local by default); polkit only for protected commands |
| After any user-root install | a fresh terminal **and** the IDE resolve the same root uno-check validated |
| Manifest unchanged, last run green | host performs **no** uno-check invocation; badge still renders from the stored report |
| Manifest moved | manifest-pinned checkups re-run; machine-state ones do not |
| Fix applied | that checkup is re-verified live, never served from cache |
| Cache older than max age | full run regardless of the gate |
| `--only` naming an unknown id | run reports that nothing matched rather than exiting silently green (P4.5) |

## Risks

| Risk | Mitigation |
|---|---|
| **False green** — uno-check validates a root nothing else uses | The invariant, P0.1 (no bare `dotnet`), P1.4 (persistence), P1.6 (divergence becomes an error) |
| **Two SDK worlds** on one machine (disk, confusion, "why do I have two dotnets") | `auto` never switches a machine that already has a working machine-wide SDK; scope is reported in output and in the structured contract |
| **VS users regressing** | `auto` selects `machine` whenever VS is detected |
| **PATH ordering fights** with other installers | The persisted-variables checkup (P1.4) detects and repairs |
| **Phase 1 landing without Phase 0** | Sequencing is explicit; Phase 0 items are individually shippable and worth landing regardless |
| **Stale green** — the machine changed outside the host (SDK uninstalled, Hyper-V disabled, JDK removed) | Max age forces a periodic full run; the displayed result always carries its timestamp (goal 5); manual re-run is always available |
| **A hardcoded never-recheck list going stale** — `openjdk` is the live example | Cacheability is derived from the recurrence classification, not a hand-maintained list; the manifest gate covers every pinned item at once |
| **Cache key missing an input** the result actually depends on | Key on identity the CLI itself reports (P4.1) rather than anything the host infers; when in doubt, invalidate |
| **Selective re-runs costing more than a full run** | One process, many `--only` (P4.4); measured at 2.55s for three checkups versus 2.59s for one |

## Alternatives considered

- **Adopt `dotnetup`.** Rejected as a dependency (see Non-goals): the in-tree script installer
  already provides the capability, and `dotnetup` is published at `daily` quality.
- **Always install user-local.** Rejected: breaks VS users, and would make the divergence the
  default state rather than an opt-in.
- **Keep machine-wide and reduce prompts by batching only.** Insufficient: it leaves the
  recurring SDK/workload path prompting on every version bump, which is the actual complaint.
- **Self-elevate the whole tool once per run.** Rejected previously for hosts; it also
  elevates work that does not need it, and the manifest question (spec 003 open question) is
  unresolved.
- **A resident background service that keeps the environment continuously checked.** Rejected
  on footprint grounds, and unnecessary: the gate is cheap enough to run at launch (P4.2).
- **A hardcoded "one-time checkups" skip list.** Superficially attractive — Hyper-V, long paths
  and Git really never change on their own. But `openjdk` looks like it belongs on that list and
  does not, and the list would need editing every time a checkup's inputs change. Deriving
  cacheability from what a check depends on (recurrence classification) costs the same and does
  not rot.
- **Caching inside uno-check.** Would spare hosts the bookkeeping, but the CLI has no
  user-scoped store, would need its own invalidation policy, and multiple hosts plus the
  terminal would contend over one cache. Exposing identity and letting each host cache is
  smaller and harder to get wrong.

## Open questions

1. On a fresh Windows machine **with** VS present but **no** SDK — does `auto` still defer to
   `machine`? Proposed yes (VS will want the machine SDK), but it is the case where a user
   would most appreciate zero prompts.
2. Should uno-check write the user `PATH` entry, or only `DOTNET_ROOT` and tell the user? PATH
   is what makes a plain terminal agree; it is also the more invasive edit.
3. ~~Is `elevation_scope` worth adding to the contract now?~~ **Resolved: no.** A host batching
   fixes needs exactly two groups — run now, or run in the one elevated child — and the shipped
   `requires_elevation` boolean already separates them. A three-valued `none | user | machine`
   would distinguish "writes nothing" from "writes user state", which no consumer acts on
   differently. Revisit only if something needs that third case. (P2.3's batching itself still
   stands; it just needs no new field.)
4. Ownership of the manifest change for OpenJDK archive URLs (P2.1).
5. What is the max age for a cached report — a day, a week? Long enough that it rarely fires,
   short enough that a machine changed outside the host self-heals without the user knowing to
   press anything.
6. Should the freshness gate be the host's conditional GET, or a `uno-check status --json`
   subcommand that answers "would a run find anything new?" without running? The subcommand
   keeps manifest resolution in one place, at the cost of ~2.4s of process startup versus 18ms
   for the host doing the GET itself. Proposed: host-side GET, with P4.1 making it possible.
7. Does the badge need per-project state? Targets are part of the cache key, so a solution
   targeting Android and one targeting desktop-only legitimately have different answers, and
   host-side direction is heading toward a Default/Project split.

## Appendix A — measurements

Reference machine: Windows 11 Pro 26200, unelevated shell, warm NuGet and dnx caches, package
`uno.check@1.35.0-json.3` from a local feed, invoked as `dotnet dnx`. Times are wall clock.

| What | Time | Notes |
|---|---|---|
| `dotnet --version` | 0.17s | host spin-up floor |
| Installed global tool, `--version` | **0.20s** | essentially the host floor |
| `dnx --version`, package cached | **0.85s** | ~0.65s of resolution, paid by *every* invocation |
| `dnx --version`, package **not** cached | **6.23s** | one-time download, per pinned version |
| `list --json` (catalog) | 3.49s first, 2.87s after | |
| One checkup (`windowshyperv`) | 2.59s | mostly startup plus the checkup's own work |
| Three checkups, one process | **2.55s** | same as one — startup dominates |
| `--only dotnetworkloads` | 2.72s | exit 1, "unknown checkup id(s)" — the id is versioned |
| `--only dotnetworkloads-10.0.201` | 6.95s | **five** results; pulls in dependencies |
| Full run, `skiadesktop` | 7.05 / 7.35 / 7.54s | median **7.35s**, no speed-up across repeats |
| Full run, `android` + `skiadesktop` | 11.95 / 10.81s | ≈ **11.4s** |
| Manifest full GET | 424ms | 6,489 bytes |
| Manifest conditional GET → `304` | **18ms** cold, ~1ms on a reused connection | the freshness gate |

Three conclusions the numbers force:

1. **There is no result cache today.** Three identical consecutive full runs took 7.05s, 7.35s
   and 7.54s — no speed-up at all. Anything that avoids re-running has to be built; the tool
   will not do it for us.
2. **Scoped re-runs must be batched.** Startup is a fixed per-invocation tax, so splitting a
   subset across processes multiplies it while one invocation naming several ids does not.
3. **The gate is effectively free.** 18ms to learn whether a 7.35–11.4s run could possibly
   discover anything is a ~400:1 return, and on the common path the answer is no.

### On launch mechanism

Three ways to start the tool unelevated on Windows, all measured above:

- **`dotnet dnx`** (what the host uses today): pins an exact version, installs nothing globally.
  Costs ~0.65s per run, plus ~5.4s once per pinned version.
- **Installed global tool with `__COMPAT_LAYER=RUNASINVOKER`**: the documented read-only mode
  (`doc/using-uno-check.md`), and what the VS Code extension already does. Verified to work when
  the variable is set in the child's environment alone — no `cmd` wrapper. Fastest, but requires
  the tool to be installed and then inherits whatever version is there. Note it *suppresses*
  elevation rather than granting it, so a fix batch must drop it and let the manifest elevate.
- **Changing the shipped manifest to `asInvoker`**: removes the need for either workaround, but
  changes behaviour for every existing CLI user. Tracked as an open question in spec 003.
