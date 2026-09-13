# Structured JSON output for GUI, CI, and agent consumers

Status: proposed — flags shipping as experimental; POC consumer in a stacked draft PR

## Problem

Hosts that want to embed uno-check — a GUI companion or IDE environment panel, CI pipelines, AI
agents — have no machine-readable surface. Today they must regex-scrape Spectre.Console
output, which is fragile and loses structure (per-check identity, fixability, suggestions).
MAUI's equivalents solved this natively: MAUI Sherpa reimplemented its doctor as a library
(code duplication we want to avoid), and the labs `maui` CLI emits a JSON `DoctorReport`.

Two further blockers for embedding:

- The exe manifest requested `requireAdministrator`, so even a read-only diagnosis run could
  not be launched from a non-elevated host process (Win32 error 740).
- There was no way to scope a run to specific checkups — only `--skip` existed — so a host UI
  could not implement per-item "Fix" buttons.

## Decision

Expose the existing engine (checkups already return `DiagnosticResult`/`Suggestion`/`Solution`
objects with status events) through a structured CLI contract instead of extracting a library
package:

1. **`--json`** — emit JSONL events on stdout, one JSON object per line, ending with a final
   `report` event. Human-readable output moves to stderr so stdout stays pure JSONL. Implies
   `--non-interactive`; the interactive self-update prompt is skipped.
2. **`--json-file <path>`** — append the same events to a file. Required for elevated child
   processes: Windows cannot redirect the stdout of a process launched with the `runas` verb,
   so the host tails the file instead.
3. **`--only <checkup-id>`** — scope the run to the named checkup(s) plus their required
   dependencies (caller ids match exactly, case-insensitively; dependency ids keep the existing
   one-way prefix rule). Repeatable. With `--fix --non-interactive`, only caller-named
   checkups are auto-fixed: dependencies are examined for context but never fixed, so a
   host-side elevation prompt always describes the fix the user requested.
4. **`--allow-elevation-prompt`** — opt a structured macOS or Linux fix run into the system
   authorization dialog (macOS administrator prompt; Linux polkit via `pkexec`). Only the
   command requested by the active solution is elevated; the Uno.Check process and its
   user-scoped path discovery remain unelevated. The option is ignored in CI and does not
   affect the existing terminal sudo flow.

### Open question — execution level (deferred, needs maintainer sign-off)

The exe manifest requests `requireAdministrator`, so the *executable* (global tool shim, dnx,
published exe) cannot start from a non-elevated host even for a read-only diagnosis (Win32
error 740). Changing it to `asInvoker` was prototyped and then **backed out of this PR**: it
alters elevation behavior for every existing CLI user and needs its own reviewed decision.

Verified mitigation in the meantime: an exe manifest does not apply when the assembly is run
through the dotnet host — `dotnet UnoCheck.dll --json …` runs unelevated today. Hosts driving
a locally built or deployed DLL are unaffected by the manifest; only invocations through the
apphost/shim (global tool, dnx) are subject to it. If the shim path must work unelevated for
hosts, the manifest change (or a self-elevate-on-fix design) should be proposed separately.

## Event schema

Modeled on the labs `maui` CLI doctor schema, adapted to uno-check ids and type names (`Report`, `Summary`, `HealthCheck`, `FixInfo` — no product-specific prefixes).
Every event carries `type`, `correlation_id` (stable per run — regenerated per invocation), and
`timestamp`.

Stream guarantees:

- In `--json` mode stdout is structurally pure: the event stream claims the captured stdout
  writer before anything else can write, and `Console.Out` is rerouted to stderr process-wide,
  so no stray write can corrupt the stream. Events are flushed per line.
- **The `report` event is emitted on every exit path** — normal completion, cancellation,
  early failures (manifest validation, unknown `--only` ids), and unhandled exceptions. An
  abnormal end reports `status: "unhealthy"` with a `reason` field. Hosts treat `report` as
  the end-of-stream marker; a stream that stops without one means the process was killed.
- The `--json-file` sink only writes files it created itself (`FileMode.CreateNew`): a
  pre-existing file or symlink at the path — stale output from a previous run — is refused
  with a warning on stderr. `CreateNew` does not defend against junctions or links in parent
  directories, which Windows follows, so hosts should point an elevated child only at a
  directory the launching user alone can write. **If that leaves no sink at all, the run
  fails fast with exit `-1`** instead of running blind while a host tails a file that never
  gets written. A sink failure mid-run (broken pipe, disk error) disables that sink, never
  the run. Hosts pass a fresh path per run and delete it afterwards.
- Skipped checkups always arrive with `status: "skipped"` and a `skip_reason` — including
  dependency-failure skips (where the failed dependency's own `error` result is what makes the
  run unhealthy) and not-applicable checkups, so every checkup counted by
  `run_started.checkup_count` resolves to exactly one final state.
- After a successful fix the checkup re-runs and emits a **second `checkup_result` for the
  same id with no second `checkup_started`**; the stream rule is last-result-per-id wins. The
  final `report` contains each checkup once, in its final state.
- `correlation_id` is stable per run and regenerated per invocation; a host launching a child
  process passes `--correlation-id <id>` so parent and child report as one logical run.

| type | payload |
|---|---|
| `run_started` | `schema_version`, `tool_version`, `channel`, `targets`, `checkup_count` |
| `checkup_started` | `id`, `name` |
| `checkup_progress` | `id`, `message`, optional `status`, optional `progress` |
| `checkup_result` | `check` — see HealthCheck below (also emitted for skipped checkups with `status: "skipped"` and `skip_reason`) |
| `fix_started` | `id`, `solution` (solution type name) |
| `fix_progress` | `id`, `message` |
| `fix_result` | `id`, `success`, optional `error` |
| `report` | `report` — the final Report |
| `checkup_catalog` | emitted by `list --json` only: `schema_version`, `checkups[]` of `{ id, name, type_name }` (`name` is the display title, as in `checkup_result`; `type_name` is the checkup class name, which `--skip` also accepts) — the applicable-checkup menu for hosts building selection UIs that feed `--only`/`--skip` |

**HealthCheck:** `id`, `name`, `status` (`ok`/`warning`/`error`/`skipped`), optional `message`,
optional `skip_reason`, optional `fix` =
`{ issue_id, description, auto_fixable, requires_elevation, args }`.
`args` is the argument vector for the per-item fix (`["--fix", "--only", "<id>",
"--non-interactive"]`), to be passed straight to the uno-check process — deliberately never a
pre-joined command string, so no host is tempted to run it through a shell (checkup ids can
embed manifest-sourced version text). Ids that fail the `[A-Za-z0-9._-]+` allowlist are never
marked auto-fixable.

`requires_elevation` answers *"will running `args` need administrator/root rights?"* — the
question a host must settle **before** launching, because Windows elevates whole processes,
not commands: without the signal the only safe choice is to elevate every fix, so
user-scoped fixes (Android SDK packages, `dotnet new` templates, the Uno SDK restore,
workloads into a user-writable SDK) raise UAC for nothing.

- True when **any** solution behind the suggestion needs elevation — a fix applies them all,
  so the host must satisfy the strictest one.
- Conservative by construction: a solution requires elevation unless it is known to write
  only user-scoped state. Layout-dependent solutions probe the real target instead of
  assuming — the same workloads fix reports `false` against a user-local `DOTNET_ROOT` and
  `true` against a Program Files SDK.
- Advice-only suggestions (no runnable solution) report `false`; they also report
  `auto_fixable: false`, so there is nothing to launch.
- It describes the *fix*, not the current process: it stays meaningful when the host is
  already elevated, and on macOS/Linux — where `--allow-elevation-prompt` elevates
  per-command rather than per-process — it is what a host uses to warn that a dialog is
  coming.

**Report (final):** `schema_version`, `correlation_id`, `timestamp`, `tool_version`,
`status` (`healthy`/`degraded`/`unhealthy`), optional `reason` (abnormal ends), `checks[]`,
`summary` = `{ total, ok, warning, error, skipped }`. Retried checkups (after a fix) appear
once with their final state.

### Versioning policy

`schema_version` appears on `run_started` and `report`. Within a major version, fields and
event types are only ever added — never renamed, removed, or retyped. Hosts must ignore
unknown event types and unknown fields. The major version bumps only on a removal or rename,
which we expect to avoid; a bump is announced in release notes.

### Exit codes

`0` all checks passed · `1` checks failed or `--only` named an unknown id · `-1` (shells show
`255`) the tool could not run (e.g. manifest validation) · `130` canceled. Structured-mode
hosts should prefer the `report` event over the exit code — the code cannot say *which*
checks failed.

## Host flow

> **Invocation caveat:** this flow assumes the host drives the assembly through the dotnet
> host (`dotnet UnoCheck.dll …`) or a deployment it controls — exe manifests do not apply on
> that path. The shipped apphost/shim (global tool, dnx) still requests
> `requireAdministrator` and cannot start unelevated until the deferred manifest decision
> above is made; a host building on the shim path today must run elevated throughout.

1. Diagnose, unelevated: `uno-check --non-interactive --json [--tfm <tfms>]`, stream stdout.
   (`--ci` additionally enables strict manifest-version validation — appropriate when the host
   pins a released tool version, wrong for local dev builds.)
2. Render per-check cards live from `checkup_*` events; summary from `report`.
3. Fix one item:
   - On Windows, launch `uno-check --fix --only <id> --non-interactive
     --json-file <fresh-path> --correlation-id <this run's id>` using `fix.args` from the
     check's result, elevating the child (`runas`) **only when that check's
     `fix.requires_elevation` is true**; tail the file for `fix_*` events and the fix
     child's own re-examined `checkup_result`. The file transport is needed either way,
     since an elevated child's stdout cannot be redirected.
   - On macOS, keep the same current-user JSON process and add `--allow-elevation-prompt`.
     User-level solutions run without a prompt. A solution that needs a protected location
     displays the system administrator dialog and elevates only its underlying command.
   - On Linux, the same flag routes protected commands through the polkit dialog (`pkexec`),
     which needs a running polkit authentication agent (present on any desktop session).
     Declining the dialog — or a missing `pkexec` — surfaces as a failed `fix_result`.
   The terminal `report` marks the end of any child stream.

   Authorization-dialog expectations for hosts:

   - **Each protected command is its own authorization.** A single fix can prompt more than
     once — e.g. a root-owned SDK's workloads fix runs repair and install through separate
     elevated commands, so two dialogs. A "fix all" flow prompts the sum across items.
     Solutions coalesce where they can (the apt-based fixes chain update+install into one
     elevated shell), but hosts should not promise one dialog per fix.
   - **The elevated command runs as root, not the user.** Per-user state the command touches
     (NuGet caches, first-run sentinels) therefore lands by `HOME`: on Linux `pkexec` sets a
     root `HOME` and scrubs the environment (the workload path re-injects what it needs via
     `env`; on-host runs show root-owned artifacts under the system dotnet root). On macOS
     the equivalent `HOME` under `do shell script … with administrator privileges` has not
     been captured yet — either outcome is benign: root's home means a cold cache (slower),
     the user's home means root-owned cache entries, same as the previous macOS `sudo` path.
     Worth recording the observed value when next on a macOS host.
   - **A cached terminal `sudo` ticket is deliberately not reused**: authorization belongs to
     the fix the user clicked, so the dialog appears even right after a terminal `sudo`.
   - **`fix.requires_elevation` is the pre-launch signal**, and it is what keeps user-scoped
     fixes prompt-free: on Windows it decides `runas` before the child starts, and on
     macOS/Linux a host can use it to warn that a dialog is coming.

Host requirements:

- Drain stderr as well as stdout, or leave stderr unredirected: in `--json` mode the whole
  human-readable UI moves there, and an undrained pipe stalls the run once its buffer fills.
- Treat process exit as an end-of-stream signal alongside `report`: an argument-parse failure
  produces no events and, on Windows, happens after the child's console has been hidden.
- `fix.args` for an item scopes the run to that item **plus its required dependencies**, but
  with `--only` present, `--fix --non-interactive` applies fixes **only to caller-named ids**
  — dependencies are examined for context, never fixed. Two host-facing consequences:
  - An item whose prerequisite is itself broken can resolve as `skipped` (dependency failure)
    instead of fixed — the card goes from "Fix" to "skipped" with no visible progress unless
    the host explains that the prerequisite must be fixed first.
  - A "fix all" (or multi-item) flow must name **every** selected id (`--only a --only b …`)
    rather than relying on dependency pull-in — which also keeps authorization prompts to
    exactly the items the user selected. A full-run `--fix` without `--only` still fixes
    everything it examines.

## Consumers

The flags ship as **experimental**: the schema is versioned and we intend the policy above,
but until a shipping host consumes it end-to-end, field-level adjustments remain possible.
A proof-of-concept GUI driving the full contract (live cards from the stream, per-item
elevated fixes over the file transport) exists as a stacked draft PR adding `UnoCheck.Gui/`
to this repo (excluded from the solution and CI); it lands separately so this contract can
merge and be reviewed on its own.

## Compatibility

- The flags are additive and the execution level is untouched (see the open question above).
  One deliberate change applies outside structured mode too: Ctrl+C now stops the run at the
  next checkup boundary with exit `130`. Previously the loop kept running to the end, because
  a checkup's `Examine` cannot observe cancellation; only an in-progress fix honored it.
- The JSONL contract is additive and versioned via `schema_version`.

## Alternatives considered

- **Extract the engine into a `UnoCheck.Core` NuGet.** Feasible (the model layer is clean) but
  costs an engine/CLI split, a TFM retarget off netcoreapp3.1/net5/net6, visibility fixes, and
  an ongoing packaging surface — all unnecessary for the host scenarios in hand. Rejected for
  now; the JSON contract does not preclude it later.
- **Reimplement checks natively in the host** (MAUI Sherpa's approach). Permanent duplication
  and drift against uno-check. Rejected.
- **Single JSON document on exit only.** Loses live per-check streaming, which the host UI
  needs for the tick-tick-tick experience. JSONL gives both; the final `report` event is the
  single-document view.
