# uno-check: `project` Command — Uno Project Structure Checkups

Issue: [unoplatform/uno.check#547](https://github.com/unoplatform/uno.check/issues/547)

## Overview & Objectives

Today uno-check validates the *environment* (SDKs, workloads, emulators) but knows nothing
about the *project* the user is sitting in. Field reality: users lose or never commit
pieces of the Uno single-project template — the `Platforms/<Head>` folders,
`Properties/launchSettings.json` profiles, the `.vscode/` folder — and the resulting
failures (build errors, missing debug profiles, broken F5 in VS Code) never name the
missing file as the cause. uno-check is the tool that can name it.

### Key objective

A new `project` command — `uno-check project [PATH]`, `PATH` defaulting to the current
directory — that, given an Uno Platform project directory (or `.csproj` path), validates
the project structure against what the project's TFMs require:

1. **Platforms folders** — `Platforms/<Head>` exists (and is non-empty) for every
   platform-specific TFM in the project.
2. **launchSettings.json** — `Properties/launchSettings.json` exists and contains the
   profiles needed by the project's TFMs.
3. **.vscode folder** — `.vscode/launch.json` + `tasks.json` present (VS Code users only).

**Phased delivery:** PR 1 ships *detection* (warnings + manual-remedy suggestions).
PR 2 ships *automated fixes* — scaffolding the missing pieces from the `unoapp` template
and copying/merging them into the project — fully designed below (P1–P5).

---

## Verified facts (investigation grounding)

| # | Fact | Consequence |
|---|------|-------------|
| F1 | `Program.Main` injects `check` as the default command when the first arg isn't a known command (`Program.cs:82-85`) — a `project` command not in that list would be shadowed (`uno-check project x` → parsed as `check project x`). | Add `project` to the `isExplicitCommand` list (one line). Accepted side effect: **older installed versions** parse `uno-check project` as `check project` and fail with a generic parse error. |
| F2 | Checkups are registered in code (`Program.cs:30-58`), no manifest entry required — precedent: `DotNetRootsCheckup`, `GitCheckup`, `HttpsDevCertCheckup` are manifest-independent. | Three new checkups are a code-only change; no manifest schema touch. |
| F3 | `CheckCommand` passes flags to checkups by seeding `SharedState` under `StateKey.EntryPoint` (e.g. `StateKey.TargetPlatforms`); checkups gate via `ShouldExamine(history)` — see `HttpsDevCertCheckup`. | The project path flows the same way: `ProjectCommand` seeds a new `StateKey.ProjectFilePath`, and the project checkups no-op (silent `Ok`) when it's absent. |
| F4 | `ListCheckupCommand` builds the graph with an **empty** `SharedState`. | Gating must live in `ShouldExamine`; keep `GetApplicableTargets` = `TargetPlatform.All` so `uno-check list` still lists the new checkups. |
| F5 | No csproj/MSBuild parsing exists anywhere in the tool. In-repo conventions: `XmlDocument`/`XDocument` + XPath for XML (`UnoSdkSolution.cs`), Newtonsoft.Json for JSON (`ConfigCommand`, manifest models). | Parse the csproj with plain XML reading (no MSBuild evaluation); parse launchSettings with Newtonsoft `JObject`. |
| F6 | `CheckCommand.ParseTfmsToTargetPlatforms` (`CheckCommand.cs:416`) maps TFMs to platform flags, but is host-OS dependent for `desktop` and collapses `maccatalyst` into `macOS`. | Project shape is host-independent — a `net9.0-desktop` TFM implies `Platforms/Desktop` on every OS. The analyzer needs its **own** TFM→folder map (reusing `NuGetFramework.ParseFolder`). |
| F7 | `--ide` skips are plain checkup-ID arrays: `Util.RiderSkips` / `VSSkips` / `VSCodeSkips` (`Util.cs:15-17`). | The `.vscode` checkup is excluded for Rider/VS users by adding its ID to `RiderSkips` and `VSSkips` — zero new machinery. |
| F8 | Fix flow: `DiagnosticResult.Suggestion` + `Solution[]`; a `Suggestion` with no solutions renders the recommendation text without a fix prompt. When a result has a Suggestion, its `Message` is **not** printed — detail belongs in `Suggestion.Description`. | v1 uses solution-less `Suggestion`s carrying the manual remedy; the fix PR later attaches `Solution`s without reshaping the checkups. |
| F9 | `UnoCheck.csproj` multi-targets `netcoreapp3.1;net5.0;net6.0`; tests are net10 xUnit reaching internals via `InternalsVisibleTo`. House testing pattern: pure `internal static` methods + temp-dir fixtures (`DotNetRootsCheckupTests`, `DotNetTargetingPackAlignmentTests`). | Analyzer logic is `internal static`, takes explicit paths, returns findings; `Examine` is a thin shell. No C# features newer than netcoreapp3.1 allows. |
| F10 | All user-controlled console text must go through `Markup.Escape` (enforced by `SpectreMarkupEscapingTests`). | Project paths/TFMs in messages are escaped. |
| F11 | `dotnet new unoapp -h` (verified against the installed Uno.Templates): `-platforms <android\|ios\|wasm\|desktop\|windows>` (multi-value, default `android\|ios\|wasm\|desktop`), `-tfm <net9.0\|net10.0>`, `-preset <recommended\|blank>`, `-renderer <native\|skia>`, `--no-update-check`; there is **no maccatalyst platform choice** in the current template. | Scaffold args are derivable from the project's TFMs; MacCatalyst fixes need a manual fallback; base TFMs outside the `-tfm` choice list can only be approximated. |
| F12 | `Util.DirectoryCopy` (`Util.cs:636`) is a plain recursive copy with no skip-existing semantics; `Solution.Implement` receives a `CancellationToken` (`Models/Solution.cs`). | Phase 2 needs a small fill-only copy helper; the scaffolding process and file I/O honor the token per AGENTS.md. |
| F13 | Spectre.Console.Cli has no declarative "option A requires option B" — options are scoped only by the settings class attached to a command, and `[CommandOption]` properties inherited from a base settings class are honored on derived commands. | Project-only options (severity escalation, template pinning, …) belong on a dedicated `ProjectSettings : CheckSettings` under a `project` command; every shared flag (`--fix`, `--ide`, `--skip`, `--ci`, `--non-interactive`, …) stays available there via inheritance, and never leaks onto `check`'s surface. |

---

## Design

### C0 — CLI + state plumbing

- **New `project` command**: `uno-check project [PATH]`.
  - `ProjectSettings : CheckSettings` (new, next to `CheckSettings.cs`) with a positional
    `[CommandArgument(0, "[PATH]")]` — a directory or `.csproj` file, defaulting to the
    current directory. Settings inheritance (F13) keeps every shared flag (`--fix`,
    `--ide`, `--skip`, `--non-interactive`, `--ci`, `--target`, `--tfm`, …) available on
    the command; **future project-only options land here and only here**, scoped in
    parsing and in `uno-check project -h`.
  - `ProjectCommand` (new, thin): resolves the path, seeds state, then delegates into the
    same orchestration `CheckCommand` runs — the checkup loop / fix flow is factored into
    a shared core both commands call, so nothing is duplicated.
  - Registered via `config.AddCommand<ProjectCommand>("project")` in `Program.cs` **and**
    added to the `isExplicitCommand` list so default-command injection doesn't swallow
    it (F1).
- Path resolution (in `ProjectCommand`, before the loop): exactly one `.csproj` (the file
  itself, or a single `*.csproj` in the directory). None or several → clear error and
  exit 1 (don't run checks against a guess).
- Seed `sharedState.ContributeState(StateKey.EntryPoint, StateKey.ProjectFilePath, path)`
  — the project checkups gate on this key (F3), so a plain `uno-check` run never
  examines them.
- **TFM scoping synergy:** under the `project` command, when neither `--target` nor
  `--tfm` was passed explicitly, feed the project's TFMs through the existing
  `ParseTfmsToTargetPlatforms` so the environment checkups are scoped to the platforms
  the project actually targets. (~5 lines, reuses existing code; see Decisions.)
- `Models/StateKey.cs`: add `public const string ProjectFilePath = "project_file_path"`.

### C1 — Project analyzer (shared logic)

`Models/UnoProjectAnalyzer.cs` — `internal static`, pure, fully unit-testable:

- `TryLoadProject(string csprojPath, out UnoProjectInfo info)`:
  - `XDocument` parse; detect Uno projects via `Sdk="Uno.Sdk[/version]"` on `<Project>`
    (or `<Sdk Name="Uno.Sdk">`). Non-Uno project → checkups report a single informative
    warning and stop (no false structure findings against arbitrary csprojs).
  - Extract `<TargetFrameworks>`/`<TargetFramework>`: split on `;`, trim. Entries
    containing MSBuild expressions (`$(...)`) are skipped as indeterminate (no MSBuild
    evaluation — F5).
- `GetExpectedPlatformFolders(tfms)` — host-OS-independent map (F6), via
  `NuGetFramework.ParseFolder(tfm).Platform`:

  | TFM platform | Expected folder |
  |---|---|
  | `android` | `Platforms/Android` |
  | `ios` | `Platforms/iOS` |
  | `maccatalyst` | `Platforms/MacCatalyst` |
  | `windows` | `Platforms/Windows` |
  | `browserwasm` | `Platforms/WebAssembly` |
  | `desktop` | `Platforms/Desktop` |
  | *(none — base `netX.0`)* | *(nothing)* |

### C2 — Platforms-folders checkup (`unoprojectplatforms`)

- `ShouldExamine`: `StateKey.ProjectFilePath` present in state.
- `Examine`: for each expected folder (C1), verify it exists **and is non-empty** under
  the project directory. All good → `Ok`. Otherwise → `Warning` with a `Suggestion`
  listing each missing folder with the TFM that requires it, and the manual remedy
  (re-create from a scratch `dotnet new unoapp` with the same name; docs link).

### C3 — launchSettings checkup (`unoprojectlaunchsettings`)

- `Properties/launchSettings.json` next to the csproj must exist and parse (Newtonsoft
  `JObject`).
- Profile coverage per TFM: the exact expected profile matrix (names / `commandName`s the
  template generates per head — e.g. `MsixPackage` vs `Project` for Windows
  packaged/unpackaged) is **pinned during implementation against a freshly scaffolded
  `dotnet new unoapp` project**, and encoded as data in the analyzer so it's trivially
  updatable. v1 rule of thumb: file exists, parses, and each platform TFM that the
  template gives a profile to has *some* matching profile — we validate presence, not
  exact content (users legitimately customize profiles).
- Missing file or missing profiles → `Warning` + solution-less `Suggestion` (F8).

### C4 — .vscode checkup (`unoprojectvscode`)

- Looks for `.vscode/launch.json` + `.vscode/tasks.json` starting at the project
  directory, walking up to the first directory containing a `.sln`/`.slnx` or `.git`
  (the template drops `.vscode` at the workspace root, which may be above the csproj).
- Registered in `Util.RiderSkips` and `Util.VSSkips` (F7) so it only runs when `--ide`
  is `vscode` or unspecified.
- Missing → `Warning` + suggestion.

### Phase 2 (PR 2) — Automated fixes

**Principle: the template is the source of truth.** All fix content comes from a freshly
scaffolded `unoapp` project — nothing is embedded in uno-check, so fixes stay in sync
with whatever Uno.Templates version the user has installed.

#### P1 — Template scaffolder (shared plumbing)

`DotNet/UnoTemplateScaffolder.cs` (internal): runs `dotnet new unoapp` into a temp
directory **once per run**, caches the scaffold path in `SharedState` so the three fixes
share one scaffold, and deletes the temp directory best-effort when the run ends.

- **Name:** `-n <RootNamespace ?? csproj-file-name>`. Naming the scaffold after the
  project's effective root namespace makes every generated namespace match the user's
  code, so files copy **verbatim** — no token rewriting.
- **Args derived from the project's TFMs** (F11):

  | Project evidence | Scaffold arg |
  |---|---|
  | base TFM `net9.0` / `net10.0` | `-tfm net9.0` / `-tfm net10.0`; a base TFM outside the choice list → nearest supported choice, stated in the fix output |
  | `-android` / `-ios` / `-browserwasm` / `-desktop` / `-windows10.*` TFM | `-platforms android` / `ios` / `wasm` / `desktop` / `windows` |
  | `-maccatalyst` TFM | `-platforms ios`; the current template has no maccatalyst choice (F11) — if the scaffold yields no `Platforms/MacCatalyst`, that folder's fix downgrades to the manual-remedy text |

  Plus `-preset blank --no-update-check`; all other options stay default. Head content
  is boilerplate that shouldn't depend on presentation/theme choices — verify during
  implementation whether `-renderer` changes head files, and if so mirror it from the
  csproj (`UnoFeatures`/renderer property).
- Runs through the existing `ShellProcessRunner` plumbing and honors the
  `CancellationToken` that `Solution.Implement` receives (F12).
- **Fails soft:** scaffold failure (template missing, `dotnet new` error) → the fix
  reports the error and falls back to the manual-remedy text; the detection result is
  unaffected. The fixes declare a non-required `CheckupDependency` on
  `dotnetnewunotemplates`, so the existing template-install checkup/solution runs first
  when Uno.Templates is missing.

#### P2 — Platforms-folders fix (attached to C2)

Copy each missing `Platforms/<Head>` from the scaffold into the project. Copies are
**fill-only**: create missing directories/files, never touch an existing file — which
also repairs the present-but-empty-folder case. New `Util.CopyMissingFiles(source, dest,
ct)` helper (F12: `Util.DirectoryCopy` can't skip existing files); every file written is
echoed via `ReportStatus`.

#### P3 — launchSettings fix (attached to C3)

- File missing → copy the scaffold's `Properties/launchSettings.json` whole.
- File present → read-merge-write, following the `ConfigCommand` global.json pattern:
  parse both as Newtonsoft `JObject`, add only the profiles whose names are missing,
  leave every existing profile untouched (a name collision means user customization —
  theirs wins), write back with `Formatting.Indented`.

#### P4 — .vscode fix (attached to C4)

Copy the scaffold's `.vscode/` into the workspace root that detection identified (the
walk-up result), fill-only via `Util.CopyMissingFiles`.

#### P5 — Wiring & safety invariants

- Each fix is a small dedicated `Solution` on its checkup's `Suggestion` —
  `CopyTemplateContentSolution` (P2/P4, parameterized by source/destination) and
  `MergeLaunchSettingsSolution` (P3). The standard flow applies unchanged: interactive
  confirm, auto-apply under `--fix --non-interactive`, and `CheckCommand`'s built-in
  re-run-after-fix re-examines the checkup, so every fix is verified by its own
  detection logic immediately.
- Invariants: fixes never overwrite or delete a user file; all writes are confined to
  the project/workspace directory (no elevation, no `WrapCopyWithShellSudo`) and the
  temp scaffold; every file written is listed in the output.

---

## Implementation map

### Phase 1 (PR 1 — detection)

| Area | Change |
|---|---|
| `UnoCheck/ProjectSettings.cs` (new) | `ProjectSettings : CheckSettings` + positional `[PATH]` argument (F13). |
| `UnoCheck/ProjectCommand.cs` (new) | Thin command: resolve/validate the project path, seed `StateKey.ProjectFilePath`, TFM-scoping synergy, delegate to the shared orchestration. |
| `UnoCheck/CheckCommand.cs` | Factor the checkup-loop/fix-flow orchestration into a shared core callable by both commands (no behavior change for `check`). |
| `UnoCheck/Models/StateKey.cs` | New `ProjectFilePath` constant. |
| `UnoCheck/Models/UnoProjectAnalyzer.cs` (new) | C1: csproj parsing, TFM→folder map, findings model. |
| `UnoCheck/Checkups/UnoProjectPlatformsCheckup.cs` (new) | C2. |
| `UnoCheck/Checkups/UnoProjectLaunchSettingsCheckup.cs` (new) | C3. |
| `UnoCheck/Checkups/UnoProjectVsCodeCheckup.cs` (new) | C4. |
| `UnoCheck/Program.cs` | Register the three checkups; `AddCommand<ProjectCommand>("project")`; add `project` to `isExplicitCommand` (F1). |
| `UnoCheck/Util.cs` | Add `unoprojectvscode` to `RiderSkips` + `VSSkips`. |
| `doc/configuring-uno-check.md`, `doc/using-uno-check.md` | Document the flag and checkups. |
| `UnoCheck.Tests/*` | See test plan. |

### Phase 2 (PR 2 — automated fixes)

| Area | Change |
|---|---|
| `UnoCheck/DotNet/UnoTemplateScaffolder.cs` (new) | P1: arg construction from project info, `dotnet new unoapp` invocation, per-run scaffold cache in `SharedState`, temp cleanup. |
| `UnoCheck/Solutions/CopyTemplateContentSolution.cs` (new) | P2/P4: fill-only copy of a scaffold folder into the project/workspace. |
| `UnoCheck/Solutions/MergeLaunchSettingsSolution.cs` (new) | P3: copy-if-missing / merge-missing-profiles. |
| `UnoCheck/Util.cs` | New `CopyMissingFiles(source, dest, ct)` helper. |
| `UnoCheck/Checkups/UnoProject*.cs` | Attach the `Solution`s to the existing `Suggestion`s; declare the non-required `dotnetnewunotemplates` dependency. |
| `doc/*` | Document `--fix` behavior for the project checkups. |
| `UnoCheck.Tests/*` | See test plan. |

---

## Test plan

**Unit (UnoCheck.Tests, temp-dir fixture pattern from `DotNetRootsCheckupTests`):**

- Analyzer: single-TFM and multi-TFM csproj; `Uno.Sdk` vs plain SDK detection;
  missing `TargetFrameworks`; MSBuild-expression TFM skipped; `Sdk` as attribute and
  as `<Sdk>` element.
- TFM→folder map: every row of the C1 table; `maccatalyst` ≠ `macos`; `desktop` maps
  identically regardless of host OS; base `net9.0` maps to nothing.
- Platforms check: all present → no findings; one missing → named with its TFM;
  present-but-empty folder → flagged.
- launchSettings: missing file; unparseable JSON; empty `profiles`; full template
  matrix → clean.
- .vscode: found in project dir; found at sln root above; missing → finding; walk-up
  stops at `.git`.
- Project resolution: directory with 0 / 1 / 2 csprojs; explicit `.csproj` path.
- Messages: paths with `[` render safely (Markup.Escape, F10).

**Unit — Phase 2 (fix logic factored as pure `internal static` methods):**

- Scaffold arg construction: TFM sets → expected `-tfm`/`-platforms` args (every row of
  the P1 table); `maccatalyst` folds into `-platforms ios`; base TFM outside the choice
  list picks the nearest and flags it; name selection uses `RootNamespace` when present,
  csproj file name otherwise.
- `CopyMissingFiles`: missing files created; existing files untouched (content
  compared before/after); nested directories; empty destination folder gets filled;
  cancellation honored mid-copy.
- launchSettings merge: no existing file → whole-file copy; existing file with a subset
  of profiles → only missing ones added; profile-name collision → user's version
  preserved byte-for-byte; malformed existing JSON → fix declines with a clear message
  (never clobbers); output stays `Formatting.Indented`.
- Scaffold-missing-head fallback: expected folder absent from scaffold (MacCatalyst
  case) → solution reports manual remedy instead of silently succeeding.

**Integration / manual QA:**

- `dotnet new unoapp` → `uno-check project` from the app folder (no path argument) →
  everything `Ok`, and environment checks scoped to the template's TFMs; same result
  with an explicit directory and an explicit `.csproj` path.
- Delete `Platforms/Android`, `Properties/launchSettings.json`, `.vscode/` → three
  warnings naming exactly those, tool exits 0.
- Non-Uno csproj → single clear "not an Uno.Sdk project" message.
- Plain `uno-check` → output byte-identical to today (project checkups silently skip);
  `uno-check project x` is not swallowed by default-command injection (F1);
  `uno-check project -h` lists inherited shared flags; `uno-check list` shows the new
  checkups and still works (F4).
- **Phase 2:** same deleted-pieces scenario + `--fix` (interactive confirm, and
  `--fix --non-interactive`) → all three restored from the template, re-run reports
  `Ok`, and the app still builds (`dotnet build`) for every TFM. Customized
  launchSettings profile survives a merge. With Uno.Templates uninstalled, the fix
  path installs templates first (dependency) or degrades to the manual message.
- `dotnet build -c Release` zero warnings (AGENTS.md); `dotnet test`.

---

## Decisions taken (flag if you disagree)

1. **Command, not flag** — `uno-check project [PATH]`. Spectre scopes options only by
   settings class (F13): a flag on `check` would force future project-only options onto
   `check`'s surface for every user, guarded by hand-rolled `Validate()` checks
   ("--x requires --project") that don't scale. `ProjectSettings : CheckSettings` keeps
   all shared flags available on the command while project-only options stay scoped.
   Accepted costs: the one-line `isExplicitCommand` addition, and older installed
   versions failing on `uno-check project` with a generic parse error (F1).
2. **Two PRs** — detection first (smallest blast radius), fixes (P1–P5) as PR 2 on top
   once the detection output is validated in the field.
3. **`Warning` severity** (exit 0) for all three checkups — structure issues shouldn't
   fail CI runs of uno-check while the checks are new; can escalate later.
4. **TFM-scoping synergy in C0** — small and high-value, but it does change which
   environment checkups run under the `project` command; easy to drop if unwanted.
5. **`.vscode` skipped for `--ide rider` / `--ide vs`** — not a defect for users of
   other IDEs.
6. **Fixes scaffold with the installed Uno.Templates version**, not one pinned to the
   project's Uno.Sdk version — the copied heads are stable boilerplate, and the
   `dotnetnewunotemplates` checkup already keeps the package current. Pinning is a
   follow-up if field evidence shows version-skew problems.
7. **Fixes are strictly additive** — never overwrite, never delete; a colliding
   launchSettings profile is treated as user customization and left alone.

## Out of scope / follow-ups

- Deeper per-file validation inside `Platforms/<Head>` (e.g. `MainActivity` present) —
  add on field evidence.
- Implicit project detection (plain `uno-check` running the project checks when the cwd
  contains an Uno csproj) — possible later; the explicit command first keeps behavior
  opt-in.
- tvOS head mapping — the template has no tvOS head today.
- Restoring `Platforms/MacCatalyst` when the installed template no longer ships that
  head (F11) — detection still reports it; the fix hands over manual instructions.
- Pinning the scaffold's template version to the project's Uno.Sdk version (decision 6).
- Repairing a `RootNamespace` ≠ project-name mismatch beyond the scaffold-naming trick
  in P1 (which already covers the common case).
