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
   one-way prefix rule). Repeatable.

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
optional `skip_reason`, optional `fix` = `{ issue_id, description, auto_fixable, args }`.
`args` is the argument vector for the per-item fix (`["--fix", "--only", "<id>",
"--non-interactive"]`), to be passed straight to the uno-check process — deliberately never a
pre-joined command string, so no host is tempted to run it through a shell (checkup ids can
embed manifest-sourced version text). Ids that fail the `[A-Za-z0-9._-]+` allowlist are never
marked auto-fixable.

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
3. Fix one item: launch elevated `uno-check --fix --only <id> --non-interactive
   --json-file <fresh-path> --correlation-id <this run's id>` using `fix.args` from the
   check's result; tail the file for `fix_*` events and the fix child's own re-examined
   `checkup_result`; the terminal `report` marks the end of the child's stream.

Host requirements:

- Drain stderr as well as stdout, or leave stderr unredirected: in `--json` mode the whole
  human-readable UI moves there, and an undrained pipe stalls the run once its buffer fills.
- Treat process exit as an end-of-stream signal alongside `report`: an argument-parse failure
  produces no events and, on Windows, happens after the child's console has been hidden.
- `fix.args` for an item scopes the run to that item **plus its required dependencies**, and
  `--fix` applies to every checkup in the run — fixing the emulator can install the Android
  SDK and JDK first. Surface that in the UI rather than promising a single-item change.

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
