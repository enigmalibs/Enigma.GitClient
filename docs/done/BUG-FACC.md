# BUG-FACC — App crashes at startup: DI cycle

## Summary

The application died before its first window: resolving `IUnhandledErrorReporter` in
`App.OnFrameworkInitializationCompleted` threw "A circular dependency was detected for the service of
type 'IAppWindows'". The cycle — `AppWindows → RepositoryOpener → AutoRefreshService → SyncOperations →
InstanceLauncher → AppWindows` — was closed by FEATURE-6693 PHASE06 (`86b71ca`), which gave
`SyncOperations` an optional `IInstanceLauncher` for its "Open <submodule>" button; the container
resolves an optional parameter whenever the service is registered.

`InstanceLauncher` needed `IAppWindows` only inside `Launch`, to open the new instance on the current
window's screen. It now takes `IServiceProvider` and resolves `IAppWindows` there, which removes the
edge that made a launcher depend on the window manager at the top of the graph. Every launcher
consumer (sync, worktrees, submodules, the panels) is freed at once, and `Launch` behaves as before.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Desktop/Services/InstanceLauncher.cs`
- Modified: `docs/roadmap.md`, `docs/plan/BUG-FACC.md`
- Created: `docs/done/BUG-FACC.md`

## Deviations & follow-ups

- None from the plan.
- Pre-existing (baseline), left alone: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows (DPAPI); it passes on Linux.
- Follow-up: the cycle shipped on the run branch because `CompositionRootTests.Container_ValidatesOnBuild`
  — which fails on it — lives in the Desktop suite, never run on this machine (BUG-6EAA). Run the
  Desktop suite on Linux before tagging. Alternatively, the composition-root tests could be allowed on
  Windows: they touch no repository, so the teardown failures that make the suite unusable here do
  not apply to them. That is the user's call.

## Build/test evidence

- **Baseline** for the run — the run branch at `04dd6a5` (the starting
  `feature/2026-11-09-feature-6693` @ `5d90409` plus the planning commit), Debug, on Windows:
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --project tests/Enigma.GitClient.Core.UnitTests`: 1508 run, 1505 passed, **1 failed**
    (the Windows-only `TokenStoreTests` one above), 2 skipped.
  - `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests`: 449 run, 447 passed,
    0 failed, 2 skipped (7 min 24 s).
  - `Enigma.GitClient.Desktop.UnitTests`: compiled, **not run** (this machine's standing rule).
- **Reproduction and fix check** — a scratch console harness (outside the repository) that builds the
  container exactly as `CompositionRootTests.BuildProvider` does (`App.ConfigureServices`,
  `ValidateOnBuild`, `ValidateScopes`), then resolves `IUnhandledErrorReporter`, `IAppWindows`,
  `IInstanceLauncher` and `ISyncOperations`:
  - on `5d90409`: `ValidateOnBuild` fails with 28 descriptors, every chain through the same cycle;
    all four resolves fail with the crash's message.
  - with the fix: `ValidateOnBuild: OK`, all four resolve; exit code 0.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1508 run, 1505 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (7 min 40 s).
  - Desktop: compiled, **not run**. `Container_ValidatesOnBuild` is the regression test for this bug;
    the harness above shows the same container validation now succeeds.
- **New tests:** none — the existing `Container_ValidatesOnBuild` already covers the cycle (decision
  in the plan).
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: `InstanceLauncher` and `IAppWindows` appear only in workflow plan/done docs. The crash
  never shipped in a release, so `RELEASENOTES.md` is unchanged.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Lookup when `IAppWindows` is missing | `GetRequiredService` | `AppWindows` is always registered; a missing registration is a wiring bug and should fail loudly | `GetService` and no screen (would hide the bug) |
| How to verify on Windows | A throwaway harness in the session scratchpad mirroring `CompositionRootTests` | The Desktop suite must not run here; the harness exercises the same container validation without that suite's teardown | Launching the GUI (unattended run; it checks less than `ValidateOnBuild` does) |
