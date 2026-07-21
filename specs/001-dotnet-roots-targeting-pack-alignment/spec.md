# uno-check: Dotnet-Roots Divergence & Targeting-Pack Alignment Checkups

Issue: [unoplatform/uno.check#542](https://github.com/unoplatform/uno.check/issues/542)
Companion (hot-reload detection/recovery in the devserver): [unoplatform/uno#23780](https://github.com/unoplatform/uno/issues/23780)

## Overview & Objectives

Field case (Ubuntu 24.04): Uno Hot Reload silently broken for `net10.0-browserwasm` while
regular builds work. Two environmental defects combined:

1. **Two dotnet roots with diverging resolution** — `PATH` resolves `/usr/bin/dotnet`
   (apt root, SDK 10.0.201, wasm manifest 10.0.108) while `DOTNET_ROOT=~/.dotnet`
   (dotnet-install root, SDK 10.0.203, wasm manifest **10.0.105**). Tooling that resolves
   through `DOTNET_ROOT`/hostfxr (devserver → Roslyn BuildHost) uses a different SDK than
   the user's terminal. The user ran `dotnet workload uninstall/install wasm-tools` to fix
   the issue — on the `PATH` dotnet, i.e. **on the wrong root**, with no effect.
2. **Stale mono-toolchain workload manifest vs installed packs** — in `~/.dotnet`,
   `sdk-manifests/10.0.100/microsoft.net.workload.mono.toolchain.current/10.0.105/` pins
   the browser-wasm chain to `Microsoft.NETCore.App@10.0.5`, but the only targeting pack on
   disk is `packs/Microsoft.NETCore.App.Ref/10.0.7/`. Regular builds survive via the
   `PackageDownload` fallback at restore; Roslyn design-time builds (hot reload) never
   restore and end up with **zero framework references and zero diagnostics** (the .NET SDK
   deliberately does not error on a missing pack when `DesignTimeBuild=true`).

This state is invisible everywhere else: builds pass, no error is emitted anywhere, and the
downstream hot-reload warning does not name the cause. uno-check is the tool that can both
**name it** and **fix it**.

### Key objective

Two new checkups:

1. **Dotnet-roots divergence** — surface multi-root installations where the effective root
   (what IDE/devserver tooling resolves) differs from the terminal's `PATH` dotnet.
2. **Targeting-pack alignment** — for the effective root, verify that every
   `Microsoft.NETCore.App` version pinned by installed mono-toolchain workload manifests
   has its targeting pack available (SDK packs folder or NuGet cache), with
   `dotnet workload update` as the automatic remediation — **on the affected root**.

---

## Verified facts (investigation grounding)

| # | Fact | Consequence |
|---|------|-------------|
| F1 | `DotNetSdk` (uno-check) already prefers `DOTNET_ROOT` when set, and records the resolved root in `SharedState`. | The "effective root" definition already exists in the codebase; the new checkups anchor on it instead of inventing a second resolution. |
| F2 | Workload manifests live per feature band under `<root>/sdk-manifests/<band>/microsoft.net.workload.mono.toolchain.current/<manifestVersion>/WorkloadManifest.json`; the pinned runtime version is readable from the manifest's pack versions (e.g. `Microsoft.NETCore.App.Runtime.Mono.browser-wasm`). | Alignment can be computed offline from the filesystem — no MSBuild evaluation required. |
| F3 | The targeting pack satisfying that pin must exist either at `<root>/packs/Microsoft.NETCore.App.Ref/<version>/` or in the NuGet cache (`~/.nuget/packages/microsoft.netcore.app.ref/<version>/`, materialized by a prior restore's `PackageDownload`). | Two probe locations; either satisfies design-time builds. |
| F4 | `dotnet workload uninstall` + `install` does **not** repair the misalignment: the manifest level stays where it is (loose manifests or pinned workload set); only `dotnet workload update` moves the manifests to match the SDK. | The remediation must be `workload update`, not reinstall — and must target the affected root explicitly (`<root>/dotnet workload update`). |
| F5 | Field evidence that users repair the wrong root when several exist (F1 vs `PATH`). | The roots-divergence checkup is a prerequisite for every other remediation being applied where it matters; it must state *which* root the tooling uses. |

---

## Design

### C1 — Dotnet-roots divergence checkup

- Enumerate candidate roots:
  - the resolved target of `dotnet` on `PATH` (symlinks followed, e.g.
    `/usr/bin/dotnet → /usr/lib/dotnet`),
  - the `DOTNET_ROOT`-family variables when set, probed in the host's order:
    `DOTNET_ROOT_<ARCH>` (net6+ hosts), then `DOTNET_ROOT(x86)` for 32-bit processes,
    then `DOTNET_ROOT`,
  - conventional locations per OS: `~/.dotnet`, `/usr/lib/dotnet`, `/usr/share/dotnet`
    (Linux), `/usr/local/share/dotnet` (macOS), `%ProgramFiles%\dotnet` (Windows).
- Report the inventory (root, SDKs present, workload-set/manifest mode) at info level.
- **Warning** when the effective root (F1) differs from the `PATH` dotnet root, stating
  explicitly: which root IDE/devserver tooling resolves, which root terminal commands hit,
  and that workload/SDK maintenance commands must target the effective root.
- No automatic fix (uno-check must not rewrite the user's environment); the value is the
  diagnosis plus the exact commands (`<effective-root>/dotnet workload update`, or aligning
  `DOTNET_ROOT`/`PATH`).

### C2 — Targeting-pack alignment checkup

For the **effective root** (F1):

1. Enumerate installed workload manifests for the band(s) of the SDK(s) present
   (`sdk-manifests/<band>/microsoft.net.workload.mono.toolchain.current/*/WorkloadManifest.json`).
2. Extract the pinned `Microsoft.NETCore.App` version for browser-wasm (F2).
3. Probe the targeting pack (F3): `<root>/packs/Microsoft.NETCore.App.Ref/<version>/` then
   the NuGet cache.
4. **Fail** when absent, with the message naming: the manifest (path + version), the pinned
   version, the packs actually installed, and the consequence (silent hot-reload breakage —
   link uno#23780).
5. **Remediation** (uno-check's standard fix flow): run `dotnet workload update` **with the
   effective root's muxer** (uno-check already drives workload operations through
   `DotNetWorkloadManager`; respect the loose-manifests vs workload-set mode). Plan B when
   `workload update` is refused/unavailable: restore a temporary minimal wasm project to
   materialize the `PackageDownload` into the NuGet cache (satisfies F3 without touching
   manifests).

Scope note: the check targets the mono-toolchain manifest (wasm) because it is the one that
pins a runtime version distinct from the SDK's bundled targeting pack; the mechanism is
written so other manifests (android, maui) can be added later if a field case shows up.

### C3 — Wiring

- Both checkups registered in the standard manifest-driven checkup list, enabled on all
  OSes (C1) and wherever wasm workloads are checked today (C2, alongside
  `DotNetWorkloadsCheckup`).
- C2 declares a dependency on the SDK checkup (needs the resolved root from `SharedState`).
- `--fix` behavior: C1 never mutates; C2 fixes via the remediation above.

---

## Implementation map

| Area | Change |
|---|---|
| `UnoCheck/Checkups/DotNetRootsCheckup.cs` (new) | C1: root enumeration, divergence detection, report. |
| `UnoCheck/Checkups/DotNetTargetingPackAlignmentCheckup.cs` (new) | C2: manifest parsing, pack probing, failure + suggested `Solution`. |
| `UnoCheck/Solutions/` (new solution/action) | `workload update` invocation bound to the effective root (reuse `DotNetWorkloadManager` plumbing); optional restore-based plan B. |
| `UnoCheck/DotNet/DotNetSdk.cs` | Expose the "effective root vs PATH root" facts needed by C1 (today it resolves but does not compare). |
| Manifest (`manifest.json` / checkup registration) | Register both checkups; C2 gated on wasm being a target platform. |
| `UnoCheck.Tests` | See test plan. |

---

## Test plan

**Unit (UnoCheck.Tests):**

- Manifest parsing: fixture `WorkloadManifest.json` files (aligned / misaligned versions,
  loose-manifest and workload-set layouts) → pinned-version extraction.
- Pack probing: temp directory layouts for `<root>/packs/...` and NuGet-cache fallback →
  present/absent classification.
- Roots enumeration: fake environment (env vars + temp dirs + fake `dotnet` executables) →
  divergence detected iff effective ≠ PATH root; symlink resolution covered.
- Message composition: failure output contains manifest path, pinned version, installed
  packs, and the per-root remediation command.

**Integration / manual QA:**

- Ubuntu with apt dotnet + `~/.dotnet` dotnet-install root + `DOTNET_ROOT` set: C1 warns
  with the expected roles; C2 fails on a deliberately-stale manifest and `--fix` repairs it
  (`workload update` on `~/.dotnet`), after which the uno#23780 hot-reload scenario works.
- Single-root machine (Windows/macOS default installs): both checkups pass quietly — no
  new noise on healthy environments.

---

## Out of scope / follow-ups

- Devserver-side detection and restore-based recovery: uno, spec
  `049-hotreload-workspace-missing-targeting-pack`
  ([uno#23780](https://github.com/unoplatform/uno/issues/23780)).
- Alignment checks for non-mono-toolchain manifests (android/maui) — mechanism ready,
  added on field evidence.
- Rewriting user environment (`DOTNET_ROOT`, `PATH`) — deliberately never done by uno-check.
