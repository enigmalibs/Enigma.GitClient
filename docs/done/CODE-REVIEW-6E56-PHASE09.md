# CODE-REVIEW-6E56-PHASE09 — [Medium] ASYNC-01 No blocking at startup

## Summary

`App.OnFrameworkInitializationCompleted` called `settings.LoadAsync().GetAwaiter().GetResult()` on the
UI thread, with the dispatcher's synchronization context installed. That is not a process edge. It
did not deadlock only because every await inside Core uses `ConfigureAwait(false)`, and because the
read is synchronous underneath anyway.

- `ISettingsService.Load()` (new) reads the settings on the calling thread under the same gate.
  `LoadAsync` is unchanged. Both share `ReadHoldingTheGate`, which reads, releases the gate and
  raises `Changed` once.
- `App` calls `Load()`. The two blocking calls in `OnExit` stay: they run at the process edge, after
  the message pump has stopped, as the review says.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Core/Configuration/SettingsService.cs` (`ISettingsService` and
  `SettingsService`)
- Modified: `src/Enigma.GitClient.Desktop/App.axaml.cs`
- Modified: `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE09.md`

## Deviations & follow-ups

- Plan step 3 ("every `ISettingsService` test double implements `Load()`") had nothing to do:
  `SettingsService` is the only implementation, and the tests use it directly.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`. The Core unit suite has been fully green
  since PHASE07.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 42 s).
  - Desktop: compiled, **not run**.
- **New tests (Core, run)** in `SettingsServiceTests`:
  - `Load` reads the file, publishes it as `Current` and raises `Changed` once;
  - `Load` keeps a corrupt file aside, as `LoadAsync` does.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Sharing the read between the two loads | A private `ReadHoldingTheGate` after each one's own wait | One read path. The sync wait does not go through a task | `LoadAsync` calling `Load` (a sync gate wait inside an async method) |
