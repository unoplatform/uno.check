# Structured JSON output for GUI, CI, and agent consumers

Status: proposed, POC implemented
Related: unoplatform/studio.live spec 084 (uno-check in Studio Desktop)

## Problem

Hosts that want to embed uno-check — Studio Desktop's environment panel, CI pipelines, AI
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
   dependencies (prefix-matched both directions, mirroring the existing dependency-resolution
   rule). Repeatable.
4. **`app.manifest`** — `requireAdministrator` → `asInvoker`. Diagnosis is read-only and needs
   no elevation; fixes that need it are run by the host in a deliberately elevated child
   (`--fix --only <id>`), and the existing interactive non-admin warning is unchanged for
   direct CLI users.

## Event schema

Modeled on the labs `maui` CLI doctor (`DoctorReport`/`FixInfo`), adapted to uno-check ids.
Every event carries `type`, `correlation_id` (stable per run), and `timestamp`.

| type | payload |
|---|---|
| `run_started` | `schema_version`, `tool_version`, `channel`, `targets`, `checkup_count` |
| `checkup_started` | `id`, `name` |
| `checkup_progress` | `id`, `message`, optional `status`, optional `progress` |
| `checkup_result` | `check` — see HealthCheck below (also emitted for skipped checkups with `status: "skipped"` and `skip_reason`) |
| `fix_started` | `id`, `solution` (solution type name) |
| `fix_progress` | `id`, `message` |
| `fix_result` | `id`, `success`, optional `error` |
| `report` | `report` — the final DoctorReport |

**HealthCheck:** `id`, `name`, `status` (`ok`/`warning`/`error`/`skipped`), optional `message`,
optional `skip_reason`, optional `fix` = `{ issue_id, description, auto_fixable, command }`
where `command` is the ready-to-run per-item fix invocation
(`uno-check --fix --only <id> --non-interactive`).

**DoctorReport (final):** `schema_version`, `correlation_id`, `timestamp`, `tool_version`,
`status` (`healthy`/`degraded`/`unhealthy`), `checks[]`, `summary` = `{ total, ok, warning,
error, skipped }`. Retried checkups (after a fix) appear once with their final state.

## Host flow (Studio Desktop)

1. Diagnose, unelevated: `uno-check --non-interactive --json [--tfm <tfms>]`, stream stdout.
   (`--ci` additionally enables strict manifest-version validation — appropriate when the host
   pins a released tool version, wrong for local dev builds.)
2. Render per-check cards live from `checkup_*` events; summary from `report`.
3. Fix one item: launch elevated `uno-check --fix --only <id> --non-interactive
   --json-file <tmp>`; tail the file for `fix_*` events; on exit re-run step 1 scoped with
   `--only <id>` to refresh that card.

## POC

`UnoCheck.Gui/` (excluded from UnoCheck.sln and CI; built from its own directory so its
net10-era `global.json` applies) — an Uno Platform desktop app that drives the contract
end-to-end: auto-runs diagnosis on launch, renders live check cards from the JSONL stream, and
wires per-card **Fix** buttons to the elevated `--fix --only` + `--json-file` tail protocol.
It prefers a locally built `UnoCheck.dll` and falls back to a global `uno-check` install.

## Compatibility

- No behavior change for existing interactive/CI users except the manifest level: the tool no
  longer self-elevates on launch. The pre-existing warning ("Administrator is required to fix
  most issues") still shows, and `--fix` runs that hit access-denied still print the
  elevation guidance.
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
