# Reducing elevation prompts

Status: proposed — phased; Phase 0 is a prerequisite for the rest

## Problem

Fixing a fresh Windows machine through `uno-check --fix` can raise up to **eight** separate
UAC prompts, because Windows elevates whole processes rather than individual commands: a host
(or the CLI) must decide before launching, and each fix that touches a protected location is
its own elevated launch.

`fix.requires_elevation` (spec 003) told hosts *which* fixes need it, which stopped the
needless prompts. It did not reduce the number of fixes that genuinely need elevation. This
spec is about that second half.

The distribution matters more than the count. Of the eight fixes that really need
administrator rights today, **three write only the .NET root** — and those three are the only
ones a developer hits *repeatedly*, on every SDK or workload bump:

| Fix | Writes | Recurring? |
|---|---|---|
| `dotnet` (SDK install) | `%ProgramFiles%\dotnet` + HKLM bundle registration | on every pinned-SDK bump |
| `dotnetworkloads-<v>` | `<SdkRoot>\{packs,sdk-manifests,metadata}` | on every workload bump |
| `dotnettargetingpacks` | same subtrees, via `dotnet workload update` | on drift |
| `openjdk` | `%ProgramFiles%\Microsoft\jdk-*` (MSI) | once per machine |
| `androidsdk` | `%ProgramFiles(x86)%\Android\android-sdk` | once per machine (location-dependent) |
| `windowslongpath` | `HKLM\SYSTEM\...\FileSystem\LongPathsEnabled` | once per machine |
| `windowshyperv` | `dism /Online /Enable-Feature` | once per machine |
| `git` | launches the VS Installer (self-elevating) | once per machine |

So the goal is not "zero prompts ever" — enabling Hyper-V or a long-path registry key is a
machine configuration change and will always require consent. The goal is that **the everyday
path costs nothing, and the one-time machine setup costs one prompt, once.**

## Goals

1. **Zero prompts on the recurring path**: SDK, workloads, targeting packs, templates, Uno SDK,
   Android SDK/emulator, dev-certs, PowerShell policy.
2. **At most one prompt for one-time machine configuration**, and only when the user explicitly
   asks for those fixes.
3. **Never report healthy for a toolchain the user's IDE or CLI does not actually use.** This
   is the hard invariant; everything below is subordinate to it.

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

**P2.4 — `git` stays as-is.** It launches the VS Installer, which self-elevates; there is
nothing for uno-check to improve.

### Phase 3 — host-side contract

- `requires_elevation` (shipped) decides whether a child is elevated.
- `elevation_scope` (P2.3) lets a host batch machine-scoped fixes into one approval and mark
  the affected rows with a shield before the user clicks.
- Hosts should surface the chosen install scope, since it changes what "your environment"
  means. A host that installs into a user root must say so.

### Phase 4 — verification

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

## Risks

| Risk | Mitigation |
|---|---|
| **False green** — uno-check validates a root nothing else uses | The invariant, P0.1 (no bare `dotnet`), P1.4 (persistence), P1.6 (divergence becomes an error) |
| **Two SDK worlds** on one machine (disk, confusion, "why do I have two dotnets") | `auto` never switches a machine that already has a working machine-wide SDK; scope is reported in output and in the structured contract |
| **VS users regressing** | `auto` selects `machine` whenever VS is detected |
| **PATH ordering fights** with other installers | The persisted-variables checkup (P1.4) detects and repairs |
| **Phase 1 landing without Phase 0** | Sequencing is explicit; Phase 0 items are individually shippable and worth landing regardless |

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

## Open questions

1. On a fresh Windows machine **with** VS present but **no** SDK — does `auto` still defer to
   `machine`? Proposed yes (VS will want the machine SDK), but it is the case where a user
   would most appreciate zero prompts.
2. Should uno-check write the user `PATH` entry, or only `DOTNET_ROOT` and tell the user? PATH
   is what makes a plain terminal agree; it is also the more invasive edit.
3. Is `elevation_scope` worth adding to the contract now, or should hosts infer "machine" from
   `requires_elevation` until a second consumer needs the distinction?
4. Ownership of the manifest change for OpenJDK archive URLs (P2.1).
