# AI Agents Contribution & Coding Instructions

This document defines strict guardrails for any AI-assisted or automated agent contributions (including Copilot, custom prompt runners, or scripted refactors). Human contributors must also ensure generated changes comply before merge.

---

## 1. Core Engineering Principles

✅ Apply all SOLID principles (SRP, OCP, LSP, ISP, DIP).
✅ Keep code simple, intention‑revealing; clarity > cleverness.
✅ Separate concerns: validation | storage | processing | error handling | presentation.
✅ Favor composition over inheritance; inject abstractions, not concretes.
✅ Optimize only with evidence (profiling/metrics).

---

## 2. Performance & Allocations

✅ Minimize allocations in hot paths (e.g. use `StringBuilder` for server-side string assembly).
✅ Avoid unnecessary LINQ in tight loops; prefer explicit loops where critical.
✅ Use `readonly` on fields and structs where possible.
✅ Avoid boxing (watch generics, interpolated logging with value types).
✅ Reuse `HttpClient`, `JsonSerializerOptions`, buffers.
✅ Only introduce `Span<T>` / `Memory<T>` when profiling shows benefit.

---

## 3. Framework & Platform Usage

✅ Apply `CancellationToken` to all async public APIs and I/O boundaries.
✅ Avoid `.Result` / `.GetAwaiter().GetResult()` outside controlled sync bridging points.
✅ Prefer non-blocking async flow (no `.GetAwaiter().GetResult()` / blocking waits) to remain WASM-safe; if a sync bridge is unavoidable, document why and keep it local.
✅ External integrations abstracted behind interfaces for testability.

---

## 4. Build & Validation

Solution: `Uno.Check.sln`

### Local Build (PowerShell)

```pwsh
# Restore dependencies
dotnet restore Uno.Check.sln

# Debug build (warnings allowed temporarily)
dotnet build Uno.Check.sln -c Debug

# Release build MUST be 100% clean
dotnet build Uno.Check.sln -c Release "/clp:WarningsOnly;Summary"
```

✅ Zero warnings in Release is mandatory.
✅ Suppress a warning only with justification in PR + targeted scope (`#pragma` with comment).
✅ Do not disable deterministic builds.
✅ Avoid expanding global `<NoWarn>` unless approved.

## 5. Workloads updates process

In order to update the workloads manifest to the latest version, you need to:

- Install the latest (or otherwise specified by user directives) .NET in a custom folder, in order to avoid inheriting the build agent's .NET SDK
- Install the following workloads:

    ```bash
    dotnet workload install ios android maui catalyst tvos wasm-tools
    ```
- Get the installed workloads list:

    ```bash
    dotnet workload list
    ```

- Update the relevant files with the new .NET SDK version and workload versions, based on the requested version:
  - `manifests/uno.ui.manifest.json` for the current .NET SDK stable release
  - `manifests/uno.ui-preview.manifest.json` for the current .NET SDK major preview release
  - `manifests/uno.ui-preview-major.manifest.json` for the next major .NET SDK preview release

## 5. Testing Requirements

✅ Every new public behavior must include tests (unit and/or integration).
✅ Namespace parity: implementation namespaces mirrored in `*.Tests` projects.
✅ AAA pattern (Arrange / Act / Assert).
✅ Deterministic fakes for time, GUID, randomness.
✅ Favor lightweight in-memory substitutes over heavy mocks.
✅ Lack of coverage for new logic blocks merge.

### Minimum Test Additions Per PR

| Change Type | Required Tests |
|-------------|----------------|
| New service/class | Happy path + 1 failure/edge case |
| New DTO or mapping | Round‑trip / validation scenario |
| Bug fix | Repro test + non‑regression guard |

### String equivalence assertions (multiline)
- Use raw strings (`"""..."""`) for expected and actual samples.
- Prefer `.Should().BeEquivalentTo(expected, o => o.IgnoringNewlineStyle())` for multiline string comparisons;
- Avoid manual newline normalization (`Replace("\r\n", "\n")`, `NormalizeLineEndings()`); rely on the AwesomeAssertions (fork of FluentAssertions) options instead.

### Run Tests

```pwsh
# Full suite
dotnet test Uno.Check.sln --no-build -c Debug

# Optional coverage (if configured)
dotnet test Uno.Check.slnx -c Debug /p:CollectCoverage=true /p:CoverletOutputFormat=lcov
```

✅ Maintain or improve passing test count
✅ Never delete tests without equivalent protection.

---

## 6. DTO & API Conventions

✅ Prefer immutable DTOs (constructor + init).
✅ Don’t expose domain entities directly.
✅ Centralize validation (FluentValidation or data annotations).
✅ Version for additive change; avoid breaking removals.
✅ Persisted enums: explicit underlying type; external protocols may prefer strings.

---

## 7. Dependency Injection

✅ Correct lifetimes: `Scoped` for request logic, `Singleton` only if stateless/thread-safe, `Transient` for lightweight.
✅ Constructor injection only (no service locator).
✅ Keep constructor params under control (< 7 ideal); refactor into options/aggregates if larger.
✅ Interfaces for externally consumed abstractions; skip when internal-only.

---

## 8. Logging & Diagnostics

✅ Structured logging (`logger.LogInformation("Processed {Id}", id)`).
✅ No PII/secrets in logs.
✅ Correct level semantics (Trace/Debug/Info/Warning/Error/Critical).
✅ Prefer injecting `ILogger<T>`; avoid static loggers.

---

## 9. Error Handling

✅ Centralized exception→response mapping (middleware/filter).
✅ Never swallow exceptions—wrap with context or let propagate.
✅ User-friendly external messages; technical details logged internally.
✅ Use RFC7807 Problem Details where appropriate.

---

## 10. Constants & Magic Strings

✅ Centralize non-trivial strings & numeric literals in `Constants`/`WellKnown`.
✅ Comment rationale for timeouts, cache durations, retry counts.
✅ Avoid scattering duplicate values.

---

## 11. Async & Concurrency

✅ All I/O-bound operations async.
✅ Honor `CancellationToken` quickly.
✅ Avoid shared mutable state; where needed protect with locks/concurrent collections.
✅ Use `ConfigureAwait(false)` only in library layers not relying on context.

---

## 12. UI / XAML (Applicable Projects)

✅ Minimize code-behind; logic in ViewModels.
✅ Use bindings/observables for state propagation.
✅ Avoid manual dispatcher usage unless necessary.

---

## 13. Security & Reliability

✅ No secrets in code; use config/secret providers.
✅ Validate input before persistence/external calls.
✅ Favor idempotency for retry scenarios.
✅ Define timeouts/retry policies explicitly for outbound operations.

---

## 14. Pull Request (Agent) Checklist

- [ ] Release build: zero warnings/errors.
- [ ] Tests added for new/changed logic (list names).
- [ ] No unjustified additions to `<NoWarn>`.
- [ ] SOLID + separation of concerns respected.
- [ ] DTOs validated; mapping tested.
- [ ] Structured logging; no sensitive data.
- [ ] Error handling consistent (middleware/filter updated if needed).
- [ ] No magic strings (constants added where needed).
- [ ] Performance considerations documented if hot path changed.
- [ ] Documentation updated (README / XML comments / changelog if needed).

---

## 15. Agent Prompting Guidance

Provide explicit constraints to reduce refactor churn:

1. Specify layer (e.g. service, controller, repository, ViewModel).
2. Define method contract (inputs, outputs, errors).
3. Request tests inline with implementation.
4. State performance expectations (no extra allocations, single pass).
5. Indicate error strategy (guard clauses vs. exceptions).

Example:
> Create `IUserProfileService` (scoped) with `Task<UserProfileDto?> GetAsync(Guid id, CancellationToken ct)` and `Task UpdateAsync(UpdateUserProfileDto dto, CancellationToken ct)`. Use DI, structured logging, validation (FluentValidation), guard clauses, and add xUnit tests (happy path + not found + validation failure).

---

## 16. Definition of Done

1. Release build warning-free.
2. Tests added & passing (including full suite).
3. Principles & conventions adhered to.
4. No unjustified performance regressions.
5. Checklist completed.

---

## 17. Exceptions Process

If a guideline cannot be met:

- Constraint
- Impact
- Mitigation / follow-up issue reference

Unexplained deviations block merge.

---

## 18. Quick Reference Table

| Area | Rule |
|------|------|
| Build | Release: zero warnings (TreatWarningsAsErrors) |
| Tests | New behavior + edge case |
| SOLID | All five applied |
| Allocations | Minimize hot paths |
| Logging | Structured; no PII |
| Errors | Central mapping; friendly messages |
| DI | Correct lifetimes, constructor injection |
| Constants | Centralize and document |
| Validation | Before processing/persisting |
| Async | Honor cancellation; avoid blocking |

---

## 19. Source Control
- Commit messages: clear, imperative, reference issues.
- MUST follow Conventional Commits format. Bullet points, no big walls of text.

---

## 20. Final Note

Agents must act deterministically and transparently. This document is the authoritative guardrail—adhere strictly to sustain maintainability, reliability, and trust.

---

(End of AGENTS Instructions)