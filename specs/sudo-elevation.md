# Sudo elevation for `dotnet workload install`

Status: accepted, implemented in `dev/agzi/issue-515-workload-context-followup`
Issue: [unoplatform/uno.check#515](https://github.com/unoplatform/uno.check/issues/515)

## Problem

On Linux/macOS, `dotnet workload install` writes into the SDK directory
(e.g. `/usr/share/dotnet`, `/usr/local/share/dotnet`). Most package-managed
installs make that path root-owned, so the install needs `sudo`.

`uno-check` runs the install inside `AnsiConsole.Status().StartAsync(...)` —
a Spectre.Console live spinner that owns the terminal and rewrites the line
roughly once per second. When the install (or sudo itself) tries to print a
`Password:` prompt, the spinner overwrites it before the user can read it.
Input still works, but the user has no idea what is being asked. The session
appears to hang.

Reproduction (issue #515 final comment):

| Mode | Password prompt visible? | Outcome |
| --- | --- | --- |
| `uno-check` (Ubuntu 22.04 VM) | no | hangs |
| `uno-check --verbose` (same VM) | yes | installs |
| `uno-check` (Ubuntu 24.04 host) | n/a (cached creds) | installs |

`--verbose` works only because `ShouldUseLiveSpinner` short-circuits when
verbose is on, so no Status block is active and the prompt is plain.

## Decision

Do the sudo handshake **before** the spinner starts, and on the happy path
**never capture the user's password** in our own process. (The
`WrapShellCommandWithSudoInteractive` fallback described below still does,
for the narrow case where the pre-handshake's cached credentials expire
before the install runs — see *What stays password-capturing*.)

Concretely:

1. `Util.EnsureSudoCredentialsCachedAsync` is called from
   `DotNetWorkloadManager.PrepareForInstallAsync`, which is invoked by
   `DotNetWorkloadsCheckup` immediately before the first
   `RunWithHeartbeat(...)` call. At this point Spectre.Console is not in a
   live state — plain `Console.WriteLine` works.
2. The helper first probes `sudo -n true`. If sudo creds are already cached
   or the user has `NOPASSWD`, it returns immediately. No prompt, no UX
   change.
3. Otherwise — and only in interactive, non-CI sessions — the helper invokes
   `sudo -v` directly. Sudo prints its own `Password:` prompt straight to
   `/dev/tty`, masks input itself, applies its own retry policy (3 attempts
   by default), and refreshes its own credential cache.
4. After `sudo -v` returns successfully, the spinner starts. The actual
   install runs under `sudo -n dotnet workload install ...` (via the
   existing `RetryWithSudo` path), which now succeeds against the freshly
   cached credentials and never prompts again.
5. If `sudo -v` exits non-zero (wrong password three times, sudo not
   installed, policy denial), `PrepareForInstallAsync` returns `false` and
   `DotNetWorkloadsCheckup` aborts the install **before** entering the
   spinner. Entering the spinner with un-cached credentials would let
   `RetryWithSudo` fall through to `WrapShellCommandWithSudoInteractive`,
   whose in-process `Console.Write("Password: ")` would be hidden by the
   live `AnsiConsole.Status` block — exactly the regression this design is
   meant to prevent. Aborting cleanly leaves the user with sudo's own error
   already on screen plus a plain-console message instructing them to
   rerun `uno-check --fix`.

The key property of the happy path: **uno-check never reads, stores, or
pipes the user's password** when the pre-handshake succeeds and the cached
credentials are still valid by the time the install runs. The
`WrapShellCommandWithSudoInteractive` fallback (see below) is the only
shipping code path that still captures the password; with the
pre-handshake in place it is rarely reached in practice.

## Rationale

This is what every well-behaved CLI tool that needs occasional elevation
already does — Homebrew, apt, dnf, the official `dotnet-install.sh`. The
user types their password into `sudo`, the program they already trust on
their machine. Sudo handles masking, retries, lecture, syslog audit, and
the tty-tickets cache.

Capturing the password ourselves on the *primary* path was strictly worse
on three axes (the same trade-offs still apply to the fallback path, which
is why we want to remove it once the pre-handshake is proven sufficient):

- **Memory exposure.** A managed `string` lives on the GC heap and cannot
  be zeroed out. A core dump or attached debugger could read it after the
  fact.
- **Trust footprint.** Any future bug or log statement in our `Util.cs`
  password code becomes a credential leak. Letting sudo handle the prompt
  removes us from the trust path entirely.
- **Reinventing UX.** Our captured-password loop had to re-implement
  retry-on-wrong-password, the lecture, masking, and `Sorry, try again` —
  all already present in sudo, all with subtle behavioral differences our
  code doesn't reproduce (e.g. tty-tickets, `pam_tally`).

## Why `sudo -v` specifically

`sudo -v` validates the user (prompting if necessary) and refreshes the
credential cache without running a command. It exits in milliseconds. There
is no long-running child process, so the macOS sudo PTY relay (`exec_pty`)
that motivated commit `200c149` is not involved — the original hang
required a child that outlived sudo's relay-stdin close window. Validation
exits cleanly before any of that matters.

## What stays password-capturing (and why)

`Util.WrapShellCommandWithSudoInteractive` is preserved. It runs the actual
`sudo dotnet workload install ...` (a multi-minute child process) using
`sudo -S` with a captured password, because that long-running case **does**
hit the macOS PTY relay hang. With the pre-handshake fix in place, this
function is now a fallback: it only runs if `sudo -n` fails after
pre-caching, which should be rare (e.g. cached creds expired between
handshake and install on a system with an unusually short
`timestamp_timeout`). A future change could drop this fallback once we are
satisfied the pre-handshake covers all practical cases.

## Alternatives considered

- **Pause/resume the Spectre.Console live spinner around an in-process
  prompt.** Spectre has no public API for this. Possible via reflection or
  by tearing the Status down and rebuilding it, but invasive and fragile.
  Rejected.
- **Detect elevation need lazily inside the install and pause the spinner
  then.** Same Spectre limitation; also still requires capturing the
  password. Rejected.
- **Always run the install as `sudo` from the start.** Forces every user
  through a sudo prompt even when their SDK is user-owned (e.g. `dotnet`
  installed via `dotnet-install.sh` into `$HOME`). Worse default UX.
  Rejected.
- **Configure `NOPASSWD` for `dotnet`.** Out of scope — that's a system
  administration choice users can make themselves; uno-check should not
  require or recommend it.

## Behavior matrix

| Platform | SDK writable | sudo -n cached | NonInteractive/CI | Result |
| --- | --- | --- | --- | --- |
| Windows | n/a | n/a | n/a | no-op |
| Linux/macOS | yes | n/a | n/a | no-op (no elevation needed) |
| Linux/macOS | no | yes | n/a | no-op (creds already cached) |
| Linux/macOS | no | no | yes | returns true (no-op); install will surface its own clear error |
| Linux/macOS | no | no | no | `sudo -v` prompts the user; on success install proceeds with `sudo -n`; on failure `PrepareForInstallAsync` returns false and the checkup aborts before the spinner |

## Files

- `UnoCheck/Util.cs` — `EnsureSudoCredentialsCachedAsync`
- `UnoCheck/DotNet/DotNetWorkloadManager.cs` — `PrepareForInstallAsync`
- `UnoCheck/Checkups/DotNetWorkloadsCheckup.cs` — call site, before
  `RunWithHeartbeat`
- `UnoCheck.Tests/DotNetWorkloadFeedbackTests.cs` — short-circuit tests
  (Windows / writable SDK / NonInteractive). The interactive `sudo -v`
  path requires a real TTY and is covered by manual QA on the issue's
  Linux VM.
