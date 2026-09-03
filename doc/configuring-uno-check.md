---
uid: UnoCheck.Configuration
---

# Configuring Uno-Check

## Running Uno-Check in a CI environment

It is possible to run Uno-Check to setup your build environment in a repeatable way by using the following commands:

## [**Windows**](#tab/windows)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip vswin --skip androidemulator --skip androidsdk
```

## [**macOS**](#tab/macos)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip xcode --skip androidemulator --skip androidsdk
```

## [**Linux**](#tab/linux)

```bash
dotnet tool install --global Uno.Check --version 1.29.4
uno-check -v --ci --non-interactive --fix --skip androidemulator
```

***

Pinning uno.check to a specific version will allow to keep a repeatable build over time, regardless of the updates done to Uno Platform or .NET. Make sure to regularly update to a more recent version of Uno.Check.

> [!TIP]
> You can use `dotnet package search uno.check` to search for the latest version of uno.check.

## Running in read-only mode on Windows

In restricted environments, it may be required to run uno-check to determine what needs to be installed without privileges elevation, without fixing the issues found.

In order to do so, use the following command:

```bash
cmd /c "set __COMPAT_LAYER=RUNASINVOKER && uno-check"
```

## Command line arguments

The following command line arguments can be used to customize the tool's behavior.

### `--target` Choose target platforms

Uno Platform supports a number of platforms, and you may only wish to develop for a subset of them. By default, the tool runs checks for all supported platforms. If you use the `--target` argument, it will only run checks for the nominated target or targets.

So, for example, the following will only check your environment for web and Linux development:

```bash
uno-check --target wasm --target linux
```

> [!NOTE]
> When specifying multiple target platforms, each element must be preceded by --target.
> It is not possible to list multiple values without this prefix.

Supported target platforms and their `--target` values:

| Target Platform  | Input Values                                  |
|------------------|-----------------------------------------------|
| WebAssembly      | `web`, `webassembly`, `wasm`                  |
| iOS              | `ios`                                         |
| Android          | `android`, `droid`                            |
| macOS            | `macos`                                       |
| SkiaDesktop      | `skiadesktop`, `skia`, `desktop`, `linux`     |
| WinAppSDK        | `winappsdk`, `wasdk`                          |
| Windows          | `windows`, `win32desktop`, `win32`            |
| All Platforms    | `all`                                         |

### `-m <FILE_OR_URL>`, `--manifest <FILE_OR_URL>` Manifest File or Url

The manifest file is used by the tool to fetch the latest versions and requirements.
The default manifest is hosted at: `https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui.manifest.json`

Use this option to specify an alternative file path or URL to use.

```bash
uno-check --manifest /some/other/file
```

### `-f`, `--fix` Fix without prompt

You can try using the `--fix` argument to automatically enable solutions to run without being prompted.

```bash
uno-check --fix
```

### `-n`, `--non-interactive` Non-Interactive

If you're running on CI, you may want to run without any required input with the `--non-interactive` argument.  You can combine this with `--fix` to automatically fix without prompting.

```bash
uno-check --non-interactive
```

### `--pre`, `--preview`, `-d`, `--dev` Preview Manifest feed

This uses a more frequently updated manifest with newer versions of things more often. If you use the pre-release versions of Uno.UI NuGet packages, you should use this flag.

The manifest is hosted by default here: [uno.ui-preview.manifest.json](https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui-preview.manifest.json)

```bash
uno-check --pre
```

### `--pre-major`, `--preview-major`

This generally uses the preview builds of the next major version of .NET available.

The manifest is hosted by default here: [uno.ui-preview-major.manifest.json](https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui-preview-major.manifest.json)

```bash
uno-check --pre-major
```

### `--dev-manifest` (alias: `--main`) Development manifest channel

This uses the latest manifest from the `main` branch of the
`uno.check` repository.

The manifest is hosted by default here:
[uno.ui.manifest.json](https://raw.githubusercontent.com/unoplatform/uno.check/main/manifests/uno.ui.manifest.json)

If the remote manifest cannot be loaded (for example due to network
restrictions), `uno-check` falls back to the embedded stable manifest and prints a warning.

```bash
uno-check --dev-manifest
```

### `--ci` Continuous Integration

Uses the dotnet-install powershell / bash scripts for installing the dotnet SDK version from the manifest instead of the global installer.

In CI mode, manifest/tool support validation runs in strict mode. In this mode,
the command fails fast if the manifest omits `check.toolVersion`, if
`check.toolVersion` has an invalid format, or if the current `uno-check`
version is older than the manifest minimum tool version, and prints an explicit
error or update message.

```bash
uno-check --ci
```

### `-s <ID_OR_NAME>`, `--skip <ID_OR_NAME>` Skip Checkup

Skips a checkup by name or ID as listed in `uno-check list`.

> [!NOTE]
> If any other checkups depend on a skipped checkup, they will be skipped too.

```bash
uno-check --skip openjdk --skip androidsdk
```

### `--only <CHECKUP_ID>` Run only specific checkups

Runs only the nominated checkup(s), plus any checkups they require. Use the argument multiple times for multiple checkups. Checkup ids (not display names) are listed by `uno-check list`; ids match exactly, case-insensitively.

```bash
uno-check --only openjdk
uno-check --fix --only androidsdk --non-interactive
```

> [!NOTE]
> An id that matches no checkup fails the run: the unknown ids are listed on stderr and the exit code is non-zero, so a typo can never produce a passing empty run.

### `--json` Structured JSONL output

Emits machine-readable JSONL on stdout — one JSON event per line (`run_started`, `checkup_started`, `checkup_progress`, `checkup_result`, `fix_started`, `fix_progress`, `fix_result`) ending with a final `report` event containing the full results and summary. The `report` event is emitted on every exit path — including cancellation and early failures — so consumers can treat it as the end-of-stream marker. Human-readable output moves to stderr so stdout stays pure JSONL. Implies `--non-interactive`.

Intended for host applications, CI pipelines, and AI agents that embed uno-check. The event schema is documented in the repository under `specs/003-structured-json-output`.

```bash
uno-check --json --target wasm > results.jsonl
```

### `--json-file <PATH>` Structured output to a file

Writes the same JSONL events to a file. Useful when stdout cannot be captured — for example an elevated child process on Windows, whose stdout cannot be redirected across the elevation boundary. Can be combined with `--json` or used alone. Implies `--non-interactive`.

The path must not already exist: uno-check only writes files it creates itself (`FileMode.CreateNew`), refusing pre-existing files and symlinks (junctions or links in parent directories are not detected, which is why the directory should be private to the launching user). If the path cannot be created and no other sink was requested, the run exits immediately with `-1` rather than running without output. Pass a fresh path per run — ideally in a directory only the launching user can write — and delete it when done.

```cmd
uno-check --fix --only androidsdk --json-file "%TEMP%\uno-check-run-1234.jsonl"
```

### `--correlation-id <ID>` Correlate structured-output runs

Structured-output events carry a `correlation_id`, newly generated per run by default. A host that launches uno-check child processes (for example an elevated per-item fix) passes its own id so the parent run and the child report as one logical run.

```cmd
uno-check --fix --only androidsdk --json-file "%TEMP%\uno-check-fix-1234.jsonl" --correlation-id 6f2c1b6e
```

### `--allow-elevation-prompt` Allow macOS / Linux authorization dialogs

Allows a structured macOS or Linux fix run to display the system authorization dialog when an individual solution needs to modify a protected location. Uno.Check itself remains in the current user's context; only the underlying command is authorized:

- **macOS** shows the system administrator authorization dialog.
- **Linux** shows the polkit authentication dialog via `pkexec` (a polkit authentication agent must be running, as on any desktop session). If `pkexec` is not installed, the fix fails with a message naming the missing tool.

Declining either dialog produces a failed `fix_result` (`Administrator approval was declined.`) and the check remains unresolved.

This option is opt-in because `--json` is also used by unattended consumers. It is ignored with `--ci`, and it does not change the existing interactive terminal `sudo` flow.

```bash
uno-check --fix --only xcode --json --allow-elevation-prompt
```

### Exit codes

| Code | Meaning |
|------|---------|
| `0`  | All checks passed (warnings possible) |
| `1`  | One or more checks failed, or `--only` named an unknown checkup id |
| `-1` (`255` in most shells) | The tool could not run — e.g. manifest validation failed |
| `130` | Canceled (Ctrl+C) |

In structured mode, prefer the final `report` event over the exit code for check results — the exit code cannot distinguish which checks failed.

### `list` List Checkups

Lists possible checkups in the format: `checkup_id (checkup_name)`.
These can be used to specify `--skip checkup_id` and `-s checkup_name` arguments.

With `--json`, emits the catalog as a single JSON line on stdout (`checkup_catalog` event with `id`, display `name`, and `type_name` per checkup; `name` matches the `name` in `checkup_result` events and `type_name` is the class name accepted by `--skip`) — for host applications building checkup-selection UIs that feed `--only`/`--skip`. Honors `--target` filtering and implies `--non-interactive`.

```bash
uno-check list --json
```

### `config` Configure global.json and NuGet.config in Working Dir

This allows you to quickly synchronize your `global.json` and/or `NuGet.config` in the current working directory to utilize the values specified in the manifest.

Arguments:

- `--dotnet` or `--dotnet-version`: Use the SDK version in the manifest in `global.json`.
- `--dotnet-pre true|false`: Change the `allowPrerelease` value in the `global.json`.
- `--dotnet-rollForward <OPTION>`: Change the `rollForward` value in `global.json` to one of the allowed values specified.
- `--nuget` or `--nuget-sources`: Adds the nuget sources specified in the manifest to the `NuGet.config` and creates the file if needed.

Example:

```bash
uno-check config --dev --nuget-sources --dotnet-version --dotnet-pre true
```
