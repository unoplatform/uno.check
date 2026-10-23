# uno-check: dotnetup-managed .NET SDK discovery and installation

Status: proposed
References:
[Introducing dotnetup](https://devblogs.microsoft.com/dotnet/introducing-dotnetup/) (2026-10-15),
[dotnetup docs](https://learn.microsoft.com/dotnet/core/tools/dotnetup),
[How dotnetup works](https://learn.microsoft.com/dotnet/core/tools/dotnetup/concepts/how-dotnetup-works),
[Environment configuration](https://learn.microsoft.com/dotnet/core/tools/dotnetup/concepts/environment)
Related specs: [sudo-elevation](../sudo-elevation.md), [001 dotnet roots](../001-dotnet-roots-targeting-pack-alignment/spec.md),
[003 structured JSON output](../003-structured-json-output/spec.md)

## Overview & Objectives

`dotnetup` is Microsoft's new user-level toolchain manager for .NET (public preview, `0.3.0-preview.1`
at the time of writing). It installs SDKs and runtimes into a user-owned root, verifies signatures and
checksums, tracks what it installed in a manifest, and can update or remove them later. It never
writes to a machine-wide root.

uno-check today acquires a missing SDK in one of two ways:

- **Native installer** (`BootsSolution` for `.pkg`, `MsInstallerSolution` for `.exe`) on macOS and
  Windows outside CI. Machine-wide root, elevation required, and every later `dotnet workload`
  operation on that root needs elevation too — which is the entire reason
  [sudo-elevation](../sudo-elevation.md) exists.
- **dotnet-install script** (`DotNetSdkScriptInstallSolution`) on Linux or with `--ci`. Writes into
  `DOTNET_ROOT`, `~/.dotnet`, or Program Files. No tracking, no signature verification, exact
  version only.

Field case (this machine, macOS arm64, 2026-10-20):

| Root | Content | How it got there | Seen by uno-check? |
|---|---|---|---|
| `/usr/local/share/dotnet` | 17 SDKs (8.0.100 … 10.0.401) | `.pkg` installers; on `PATH` | yes (known location + `PATH`) |
| `~/Library/Application Support/dotnetup/dotnet` | SDK 10.0.402 | `dotnetup sdk install latest` | **no** |

dotnetup's env mode is unconfigured on this machine (`dotnetup env show` → *No dotnetup env
configuration found*), so neither `PATH` nor `DOTNET_ROOT` points at the dotnetup root. uno-check
has no idea that root exists: it is not in `DotNetSdk.KnownDotnetLocations`, not in
`DotNetRootsCheckup.EnumerateKnownRoots`, and nothing reads the dotnetup manifest. As dotnetup
adoption grows, this becomes the common shape of a developer machine, and uno-check will report
"SDK missing" on machines where the SDK is installed, then offer to install it a second time with
the `.pkg`.

### Key objective

1. **See** dotnetup-managed roots: inventory them, report them in the roots checkup, and name the
   env-mode gap when a required SDK is present there but invisible to tooling.
2. **Install through dotnetup** when it is the right tool: a new `Solution` that drives
   `dotnetup sdk install` non-interactively, into a user-owned root, with no elevation — and makes
   every subsequent workload fix in the same run elevation-free as a consequence.
3. Keep the existing installers as-is; dotnetup is additive and opt-in while it is in preview.

### Non-goals

- Replacing the native installers on Windows machines that rely on the Visual Studio SDK, or on
  any machine where the user wants a machine-wide root.
- Rewriting the user's shell profile, `PATH` or `DOTNET_ROOT`. Spec 001 records this as a standing
  rule for uno-check; it holds here too. dotnetup can do it (`dotnetup env set shell`) — uno-check
  tells the user the command, it does not run it.
- Managing workloads through dotnetup. It does not support them yet (roadmap item).
- Installing runtimes (`dotnetup runtime install …`). Nothing in the manifest needs a bare runtime.

---

## Verified facts (investigation grounding)

Observed on `dotnetup 0.3.0-preview.1.26511.1` (osx-arm64) unless marked *docs*.

| # | Fact | Consequence |
|---|------|-------------|
| F1 | Default managed root: macOS `~/Library/Application Support/dotnetup/dotnet`, Windows `%LOCALAPPDATA%\dotnetup\dotnet`, Linux `$XDG_DATA_HOME/dotnetup/dotnet` or `~/.local/share/dotnetup/dotnet`. The data directory moves with `DOTNET_DOTNETUP_DATA_DIR`; `--install-path` picks any other root. | Hard-coding the three default paths is a fallback only; the authoritative inventory is the dotnetup manifest. |
| F2 | `dotnetup list --format json --no-verify` prints `{ installSpecs: [{component, versionOrChannel, source, globalJsonPath, installRoot, architecture}], installations: [{component, version, installRoot, architecture, frameworkName}] }`. `--no-verify` skips per-file validation (fast, no disk walk). | One process call yields every managed root and every installed SDK version with its architecture. Parse with a tolerant DTO (unknown fields ignored — preview schema). |
| F3 | `dotnetup --info --format json --no-list` prints `{version, commit, architecture, rid, …}` and exits 0. | Cheap presence/version probe. |
| F4 | The binary lives at `~/.dotnetup/dotnetup` (bootstrap script default) and is normally on `PATH` only through the user's shell profile. In this session `command -v dotnetup` failed while the binary existed. | Probe `~/.dotnetup/dotnetup` (and `%USERPROFILE%\.dotnetup\dotnetup.exe`) explicitly, then `PATH`. Never assume `PATH`. |
| F5 | A tracked install **fails** if the target already contains a .NET installation not recorded in the selected manifest (*docs*). `--untracked` bypasses this but forfeits list/update/uninstall. | uno-check cannot point dotnetup at an existing `~/.dotnet` or `/usr/local/share/dotnet`. Managed root or nothing. |
| F6 | dotnetup refuses to write to system-managed roots (Program Files, `/usr/share/dotnet`, `/usr/local/share/dotnet`) (*docs*). | It cannot replace the native installers for machine-wide layouts. |
| F7 | Channel grammar: `latest`, `lts`, `preview`, `daily`, `10`, `10.0`, `10.0.1xx`, `10.0.1xx-daily`, exact `10.0.103`. An unrecognised channel exits **1** with a one-line error on stderr. No `--arch`; installs for the dotnetup process architecture. | Exit code non-zero ⇒ failure, surfaced through the existing `Util.ThrowIfFailed` message pattern. No cross-arch installs (x64 SDK on arm64 Mac is out). |
| F8 | With no channel argument, `sdk install` reads the nearest `global.json` from the **current directory** upward and falls back to `latest`. | Always pass the channel explicitly. uno-check runs from arbitrary directories. |
| F9 | `--interactive` and `--no-progress` exist; `--interactive` defaults to false when output is redirected or in CI. `--set-default-install` is parsed but not applied by the current install handler (*docs*). | Invoke as `sdk install <v> --install-path <root> --interactive false --no-progress`. Do not rely on `--set-default-install`. |
| F10 | Env modes: `none` (nothing changed; use `dotnetup dotnet …`), `shell` (edits the shell profile; macOS/Linux recommended), `everywhere` (Windows only, default there; edits the **system** `PATH` under UAC and shadows Program Files for every process, so VS-installed SDKs vanish unless `--migrate-from-system` is also run). `dotnetup env script` emits the `export DOTNET_ROOT=… PATH=…` lines for the current shell. | uno-check never selects `everywhere`. On macOS/Linux a `shell`-mode root is visible to terminals but **not** to IDEs or the Uno devserver launched from the Dock/Finder (they resolve `DOTNET_ROOT`-first per spec 001) — the roots checkup must say so. |
| F11 | Bootstrap: `curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh \| bash` / `irm https://aka.ms/dotnetup/get-dotnetup.ps1 \| iex`; installs the latest **preview** build into `~/.dotnetup` and verifies SHA-512. No stable channel yet. | Same fetch-script-then-run pattern as `DotNetSdkScriptInstallSolution` today; same trust level as the dotnet-install script it would replace. |
| F12 | `DotNetWorkloadsCheckup` captures `SdkRoot` in its **constructor**, which `CheckupManager.BuildCheckupGraph` runs before any checkup examines. `DotNetCheckup` writes `DOTNET_SDK`/`DOTNET_SDK_VERSION` to `SharedState` during `Examine`, after that capture. | Any installer that creates a *new* root mid-run (dotnetup default root, or `~/.dotnet` from the script when nothing existed before) leaves the workloads checkup pointed at the pre-install root. Must be fixed for D3 to be useful; it is a latent bug for the script path today. |
| F13 | After a successful fix the host re-runs the same checkup ("Fix applied. Checking again…"); `DotNetSdk` re-resolves the root from `SharedState["DOTNET_ROOT"]` on construction. | A solution that sets `SharedState["DOTNET_ROOT"]` to the root it installed into makes the re-examination find the SDK without touching the process environment. |
| F14 | The checkup's satisfaction rule is `installed >= manifest version` unless `requireExact`. The manifest's `DOTNET_SDK_VERSION` is kept current by spec 002 drift automation. | Installing the **exact** manifest version is deterministic and always satisfies the check. A floating band (`10.0.1xx`) can resolve to an older release than the manifest when the manifest leads Microsoft's publish (preview manifests) and would then fail the re-check. |
| F15 | User-owned root ⇒ `Util.IsDirectoryWritable(root)` is true ⇒ `DotNetWorkloadUpdateSolution.RequiresElevation` and the workloads install path report **false**, and `--json` hosts see `requires_elevation: false`. | No new elevation code. The sudo pre-handshake and PTY relay in [sudo-elevation](../sudo-elevation.md) simply never engage on such machines. |

---

## Design

### D1 — Discovery: a dotnetup inventory service

New `DotNet/DotNetUpLocator` (sealed, injectable; `IDotNetUpLocator` for tests):

- `TryFindExecutable()` — `~/.dotnetup/dotnetup[.exe]`, then `PATH`. Result cached per run in
  `SharedState` (`dotnetup_exe`, `dotnetup_version`).
- `GetInventoryAsync(ct)` — runs `list --format json --no-verify`, deserialises F2 into immutable
  DTOs (`DotNetUpInstallSpec`, `DotNetUpInstallation`), returns the distinct `installRoot` set
  plus installed SDK versions per root. Any failure (missing binary, non-zero exit, schema change)
  yields an **empty** inventory with a `Util.Log` line — never an exception. Discovery is advisory.
- `GetEnvMode()` — reads `dotnetup.config.json` in the data directory (F1), which stores the access
  mode; a missing file means unconfigured (observed on this machine: no file, and `env show` reports
  no configuration). Falls back to parsing `env show` text when the file exists but has no
  recognisable mode. Anything else maps to `unknown`, and the guidance text below degrades to
  "run `dotnetup env show`". The config file's field names are not documented; the parser is
  tolerant and covered by a fixture captured from a real `env set shell` run during implementation.

Consumers:

- `DotNetSdk` appends the inventory roots (and the F1 defaults, deduplicated) to
  `KnownDotnetLocations`, **after** the existing entries. Resolution precedence is unchanged:
  `--dotnet` > `DOTNET_ROOT` env > hostfxr (`GetDotnetExeDirectory`) > known locations. A dotnetup
  root wins only when nothing else resolves — exactly the "fresh machine, dotnetup only" case —
  because when something else resolves, *that* is what IDE tooling sees, and uno-check reports
  reality, not preference.
- `DotNetRootsCheckup.EnumerateKnownRoots` includes the inventory roots and labels them
  `Found .NET root: <path> (dotnetup-managed, env mode: <mode>)`.
- New advisory branch in `DotNetCheckup`: when the required SDK is **missing** from the effective
  root but **present** in a dotnetup root whose env mode does not expose it, the diagnostic stays
  `Error` (the SDK really is unreachable for tooling) but the suggestion description leads with
  the one-line remedy before offering any installer:

  > SDK 10.0.108+ is installed by dotnetup at `<root>` but not visible to tooling
  > (env mode: none). Run `dotnetup env set shell` and open a new terminal, or run uno-check
  > with `--dotnet "<root>"`.

### D2 — Installer selection

New option on `CheckSettings`:

```
--sdk-installer <auto|dotnetup|script|native>
```

| Value | Behaviour |
|---|---|
| `native` | Today's non-CI behaviour (`.pkg`/`.exe`). |
| `script` | Today's `--ci`/Linux behaviour (dotnet-install into `DOTNET_ROOT`/`~/.dotnet`/Program Files). |
| `dotnetup` | D3. Bootstraps dotnetup if absent. |
| `auto` (default) | dotnetup **only if already installed** (F4) **and** its env mode is `shell` or `everywhere`, and not on Windows-with-VS; otherwise today's selection (`script` for CI/Linux, `native` elsewhere). |

Rationale for `auto`: a user who installed dotnetup *and wired it in* has chosen the user-local
model; giving them a `.pkg` is the surprising answer. A user who has not should not get a preview
tool bootstrapped onto their machine by a health check. The env-mode condition exists because of
this machine's field case: dotnetup present, mode unconfigured. Installing there would land an SDK
that no terminal or IDE can see, and the D1 advisory would then report the install uno-check just
made as a problem. In that state `auto` falls back to today's installer and the advisory names
`dotnetup env set shell` as the alternative; `--sdk-installer dotnetup` still forces the managed
install for users who intend to wire it in afterwards. `--ci` keeps meaning `script` unless
`--sdk-installer dotnetup` is passed, because the documented `--ci` contract names the scripts.

The Windows-with-VS exclusion: `auto` stays `native` when Visual Studio is installed. The SDK
checkup runs before the VS checkup (`dotnet` is a dependency of most checkups), so it cannot read
that checkup's result; it calls the VS checkup's existing static `vswhere` probe
(`VisualStudioWindowsCheckup.GetWindowsInfo`) directly instead of adding a dependency edge that
would reorder the graph. A dotnetup root in `shell`/`none` mode is invisible to VS; `everywhere` mode shadows the
VS SDK (F10). Neither is a good default outcome of a tool whose users are largely on VS.

The existing `--dotnet <SDK_ROOT>` keeps its meaning (effective root for the run). Under
`--sdk-installer dotnetup` it is passed as `--install-path` **only if** the directory is empty or
missing (F5); otherwise the solution reports why it is falling back to the managed default root.

### D3 — `DotNetUpSdkInstallSolution`

Constructor: `(string version, bool requireExact, SharedState state, IDotNetUpLocator locator)`.
All inputs resolved at construction (same reasoning as `DotNetSdkScriptInstallSolution`: the
elevation answer and the install target must describe the same thing).

- `RequiresElevation => false`. Always user-scoped (F6). The `--json` host sees it (F15).
- `InstallRoot`: `--dotnet` if empty/missing, else the F1 default for the platform. Exposed
  `internal` for tests and for the status line.
- `Channel`: the **exact** manifest version, for both `requireExact` values (F14). Band tracking is
  a deliberate non-choice, see *Alternatives*.
- `Implement`:
  1. Locate dotnetup (F4). If absent and installer is `dotnetup` (explicit), download the bootstrap
     script for the platform to the temp directory and run it with `bash`/`powershell`, mirroring
     the script solution. If absent under `auto`, this solution was never offered (D2).
  2. Run `<dotnetup> sdk install <version> --install-path <root> --interactive false --no-progress`
     via `Util.ShellCommand` (which builds a `ShellProcessRunner` with `UseSystemShell = false` and an
argument list) with the cancellation token. No shell, no sudo. Non-zero exit ⇒
     `InvalidOperationException` with the tail of stdout+stderr (`Util.BuildCommandFailureMessage`).
  3. Set `SharedState["DOTNET_ROOT"] = InstallRoot` so the re-examination (F13) and every
     downstream checkup target the new root. Record `sharedState.ContributeState(this,
     "dotnetup_installed_root", InstallRoot)` for the roots checkup.
  4. Report, in plain console text (not inside a spinner — nothing here needs one):

     ```
     Installed .NET SDK 10.0.108 to <root> (dotnetup-managed).
     This terminal and IDE sessions do not see it yet. To make it the default for new shells:
         dotnetup env set shell
     For this terminal only:
         eval "$(dotnetup env script)"        # bash/zsh
         dotnetup env script --shell pwsh | Invoke-Expression
     Or pass --dotnet "<root>" to uno-check.
     ```

     uno-check does not run `env set` (non-goal). On Windows the text names `shell`, never
     `everywhere`.

### D4 — Make the workloads checkup follow the root chosen during the run (F12)

`DotNetWorkloadsCheckup` resolves `SdkRoot` lazily at `Examine` time from
`SharedState["DOTNET_ROOT"]` (falling back to the constructor-time value). `Id`/`Title` keep
using the manifest SDK version, so dependency sorting and `--only` ids are unchanged. This also
fixes the latent script-path bug on a machine with no prior SDK. `DotNetTargetingPackAlignmentCheckup`
already reads the root at examine time; verify in tests rather than assume.

### D5 — Elevation and workloads

No new code. With a user-owned root the probes in `DotNetWorkloadsCheckup`/`DotNetWorkloadManager`
and `DotNetWorkloadUpdateSolution.RequiresElevation` return false (F15); the sudo pre-handshake
is skipped; `dotnet workload install` runs unprivileged. This is the primary user-facing payoff:
**a Linux/macOS user who installs the SDK through uno-check never types a password**, for the SDK
or for the workloads.

### D6 — Structured output (spec 003)

- `fix_started.solution` carries `DotNetUpSdkInstallSolution`; `fix.requires_elevation` is `false`.
- `--sdk-installer <value>` is appended to `fix.args` whenever it was passed, next to the existing
  `--dotnet <root>` forwarding in `CheckCommand`. Without this a host relaunching the per-item fix
  would silently get the native installer.
- `checkup_result` for `dotnetroots` gains nothing structural; the dotnetup annotation is in the
  existing message text. A `roots[]` array with `{path, managed_by, env_mode}` is a natural
  additive follow-up if a host wants it; not in this spec.

### D7 — Manifest

No schema change required: the version is already there. Optional additive field on the SDK
entry, `"dotnetupChannel": "10.0.1xx"`, lets a future manifest opt a channel into band tracking
once F14's concern is handled (e.g. by validating `installed >= manifest` after install and
falling back to the exact version). Parsed, ignored unless set, not used by the shipped manifests.

---

## Behaviour matrix

| Machine | `auto` picks | SDK root after fix | Elevation (SDK) | Elevation (workloads) |
|---|---|---|---|---|
| macOS, `.pkg` SDKs only, no dotnetup | native (`.pkg`) | `/usr/local/share/dotnet` | yes | yes |
| macOS, dotnetup present, env `shell` | dotnetup | `~/Library/Application Support/dotnetup/dotnet` | no | no |
| macOS, dotnetup present, env `none` (this machine) | native (`.pkg`); D1 advisory names `dotnetup env set shell` | `/usr/local/share/dotnet` | yes | yes |
| macOS, same, `--sdk-installer dotnetup` | dotnetup | managed root; guidance printed | no | no |
| Linux, apt dotnet on `PATH`, no dotnetup | script | `~/.dotnet` | no | no (user root) |
| Linux, `--sdk-installer dotnetup` | dotnetup (bootstraps) | `~/.local/share/dotnetup/dotnet` | no | no |
| Windows + VS, dotnetup present | native (`.exe`) — VS exclusion | Program Files | UAC | UAC |
| Windows, no VS, dotnetup present | dotnetup | `%LOCALAPPDATA%\dotnetup\dotnet` | no | no |
| CI (`--ci`) | script, unless `--sdk-installer dotnetup` | as today | as today | as today |
| Any, `--dotnet <non-empty foreign root>` + dotnetup | dotnetup, falls back to managed root with a notice (F5) | managed root | no | no |

---

## Implementation map

| Area | Change |
|---|---|
| `UnoCheck/DotNet/DotNetUpLocator.cs` (new) + `IDotNetUpLocator` | D1: executable probe, `list --format json` inventory, env-mode probe, DTOs. |
| `UnoCheck/DotNet/DotNetSdk.cs` | Append dotnetup roots to `KnownDotnetLocations` (after existing entries). |
| `UnoCheck/Checkups/DotNetRootsCheckup.cs` | Include and label dotnetup roots in the inventory lines. |
| `UnoCheck/Checkups/DotNetCheckup.cs` | Installer selection (D2); env-gap advisory (D1); offer `DotNetUpSdkInstallSolution`. |
| `UnoCheck/Solutions/DotNetUpSdkInstallSolution.cs` (new) | D3. |
| `UnoCheck/Checkups/DotNetWorkloadsCheckup.cs` | Lazy root resolution (D4). |
| `UnoCheck/CheckSettings.cs`, `CheckCommand.cs` | `--sdk-installer`; thread the enum through `SharedState`; forward it in `fix.args` (D6). |
| `UnoCheck/WellKnown`/constants | dotnetup default data dirs, bootstrap URLs, executable name. |
| `doc/configuring-uno-check.md`, `README.md` | Document `--sdk-installer`, the `--ci` interaction, and the env-mode guidance. |
| `UnoCheck.Tests` | See test plan. |

---

## Test plan

**Unit (UnoCheck.Tests, AAA, no network, no real dotnetup):**

- `DotNetUpLocatorTests`: executable probe order (home dir before `PATH`) with a fake file system;
  JSON inventory parsing from fixtures (one root / several roots / custom `--install-path` root /
  unknown extra fields / empty arrays / malformed JSON ⇒ empty inventory, no throw); env-mode
  parsing for the four known outputs and an unknown one.
- `DotNetUpSdkInstallSolutionTests`: `RequiresElevation` is always false; install-root
  precedence (`--dotnet` empty dir ⇒ used, `--dotnet` non-empty ⇒ managed default with notice,
  none ⇒ default per platform, `DOTNET_DOTNETUP_DATA_DIR` honoured); argument vector is exactly
  `sdk install <v> --install-path <root> --interactive false --no-progress`; non-zero exit ⇒
  exception carrying the output tail; success ⇒ `SharedState["DOTNET_ROOT"]` equals the root.
- Installer selection: table-driven over (platform, CI, dotnetup present, env mode, VS present,
  `--sdk-installer`) ⇒ solution type; unconfigured env mode under `auto` ⇒ today's installer.
- `fix.args` composition: `--sdk-installer dotnetup` round-trips into the per-item fix vector.
- `DotNetCheckup` advisory: required SDK absent from effective root, present in a dotnetup root
  with env mode `none` ⇒ `Error` whose suggestion text names the root and `dotnetup env set shell`.
- `DotNetWorkloadsCheckup` lazy root: constructor root A, `SharedState` later set to B ⇒ examine
  uses B.
- Roots checkup: dotnetup root appears with the `dotnetup-managed` label; divergence logic
  unchanged when the dotnetup root is neither effective nor on `PATH`.

**Integration / manual QA:**

- This machine as-is: `uno-check` reports the dotnetup root in `dotnetroots`; no change in
  outcome (10.0.401 in the effective root already satisfies 10.0.108).
- This machine with `--dotnet "$HOME/Library/Application Support/dotnetup/dotnet"`: effective
  root is the dotnetup one; workload fixes run without sudo.
- Fresh macOS VM, dotnetup installed, no other SDK, `uno-check --fix`: SDK lands in the managed
  root, guidance printed, workloads install unprivileged, re-examination passes.
- Fresh Ubuntu container, `uno-check --fix --sdk-installer dotnetup --non-interactive`: bootstrap
  + install + workloads with no password prompt.
- Windows + VS: `auto` still offers the `.exe`; `--sdk-installer dotnetup` installs to
  `%LOCALAPPDATA%` and the guidance names `shell`, never `everywhere`.
- `--json` run of the above: `fix.requires_elevation=false` for the SDK fix.

---

## Rollout

1. **Phase 1 (this spec):** D1 discovery always on; D3 behind `--sdk-installer dotnetup`; `auto`
   as defined in D2. dotnetup stays preview — the solution carries the version it was validated
   against in a constant and logs a warning (not a failure) when `--info` reports a different
   major/minor.
2. **Phase 2 (dotnetup GA):** `auto` prefers dotnetup on macOS/Linux even when not yet installed
   (bootstrap by default), keeping `native` on Windows+VS. Revisit the band-tracking channel (D7).
3. **Phase 3 (dotnetup gains workload support):** evaluate routing workload installs through
   dotnetup; out of scope until it exists.

---

## Alternatives considered

- **Track a feature band (`10.0.1xx`) instead of the exact version.** Gives users
  `dotnetup update`. Rejected for phase 1 per F14: the manifest is the source of truth and can
  lead the public band; a floating channel can fail the re-check. Left as the D7 opt-in.
- **Install into the existing root (`~/.dotnet`) with `--untracked`.** Keeps one root, but
  forfeits everything dotnetup adds over the dotnet-install script except signature checks.
  Rejected; if the user wants that layout, `--sdk-installer script` still exists.
- **Run `dotnetup env set shell` after installing.** Would make the SDK visible without user
  action. Rejected: uno-check does not rewrite shell profiles (spec 001), and on Windows the
  equivalent default is `everywhere`, which breaks VS-dependent builds.
- **Make dotnetup the default everywhere now.** Rejected while it is preview with no stable
  bootstrap channel (F11) and the Windows/VS interaction (F10) is unresolved upstream.
- **Prefer the dotnetup root over the hostfxr-resolved root when both exist.** Rejected: uno-check
  reports what tooling resolves (spec 001 "effective root"); preferring a root tooling cannot see
  would hide exactly the problem D1 is meant to surface.

---

## Open questions

- Should `auto` also exclude Rider/VS Code users on Windows, or only VS? VS is the only IDE that
  ships and depends on its own Program Files SDK; proposal: VS only.
- dotnetup prints the resolved version only in text output; `sdk install` has no `--format json`.
  Re-reading `list --format json` after install is the reliable way to confirm what landed.
  Proposal: do that, and log it.
- Per-architecture: on an arm64 Mac the manifest may one day need an x64 SDK (Rosetta
  scenarios). dotnetup cannot do that (F7); the native installer path must remain for it.

## Out of scope / follow-ups

- Runtime installs through dotnetup.
- Workloads through dotnetup (roadmap upstream).
- A structured `roots[]` payload in `--json` (D6).
- Migration of existing machine-wide installs into dotnetup (`--migrate-from-system`); a user
  decision, not a health-check action.
