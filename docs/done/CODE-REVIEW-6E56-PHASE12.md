# CODE-REVIEW-6E56-PHASE12 — [Medium] ASYNC-02 Hosted refresh loops

## Summary

The periodic refresh (`AutoRefreshService`) and the file-system watcher (`RepositoryWatcher`) started
their work as a side effect of being constructed. That is why `App` had to resolve
`IRepositoryWatcher` and throw the result away to get it running. Their lifetime followed
construction order and container disposal instead of the host's start and stop, and neither logged
its start or its stop.

- Both implement `IHostedService`. The subscriptions (repository, writes, settings) and the first
  schedule moved from the constructor to `StartAsync`, which is idempotent: a second start does
  nothing.
- `StopAsync` unsubscribes and cancels the loop. `Dispose` does the same once and marks the service
  disposed.
- Start and stop are logged at `Debug`.
- Registration: the concrete singleton, the interface over it, and
  `AddHostedService(provider => provider.GetRequiredService<X>())`. The host starts the very
  instance every caller holds.
- `App` builds the host, attaches the safety net, reads the settings (PHASE09's `Load()`) and applies
  the theme, and only **then** calls `host.Start()`. The hosted services thus start on the UI thread
  with the settings already read. The forced `GetRequiredService<IRepositoryWatcher>()` is gone.
  Before this dev the host had no hosted service, so starting it later changes nothing else.
- Test infrastructure: a test container has no host.
  - `TestServices.Build` starts the automatic refresh, as the app's host does. It runs on the manual
    clock and is inert until a test moves it.
  - `TestServices.Started<T>()` resolves a service and starts it when it is hosted. The watcher
    integration test uses it, and `RepositoryWatcherTests`' harness starts the watcher it builds.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Desktop/Services/AutoRefreshService.cs`
- Modified: `src/Enigma.GitClient.Desktop/Services/RepositoryWatcher.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `src/Enigma.GitClient.Desktop/App.axaml.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/TestServices.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryWatcherTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryWatcherIntegrationTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE12.md`

## Deviations & follow-ups

- **`StopAsync` cancels the loop but does not await it.** The plan said "cancels and awaits the
  loop". The loops resume on the UI thread's context, and `App.OnExit` stops the host from the UI
  thread with `GetAwaiter().GetResult()` once no message pump remains, so awaiting the loop there
  would never return. Cancelled, each loop ends at its next resumption and disposes its own timer and
  token source, as before.
- `host.Start()` moved after the settings read (the plan kept it first, relying on `Changed`). With
  the services' constructors no longer starting anything, it is simpler, and safer, to start them
  once the settings are known.
- A loop that dies still logs one error and stays dead until a repository or setting changes. This is
  unchanged: the review listed it under "why it matters", but the fix it recommended is the hosted
  lifetime, done here.
- The test container now constructs the automatic refresh in every Desktop test (before, it was
  constructed only by tests that resolved something depending on it). It is inert on the manual
  clock.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 35 s).
  - Desktop: compiled, **not run**.
- **Host check (throwaway harness outside the repository):**
  - `App.BuildHost()` lists the hosted services `AutoRefreshService` and `RepositoryWatcher`, the same
    instances as `IAutoRefreshService` / `IRepositoryWatcher`;
  - `host.Start()` takes 19 ms; with no repository, neither loop runs;
  - `host.StopAsync()` returns in 3 ms;
  - the container validates on build.
- **New tests (Desktop, compiled, not run)** in `CompositionRootTests`:
  - the two loops are hosted services shared with their callers;
  - building the container starts nothing.
- **Adapted tests (compiled):** the hand-built watcher is started in `RepositoryWatcherTests`; the
  integration test starts the container's watcher.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes how the loops start.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Await the loop in `StopAsync` | No: cancel only | The host is stopped from the UI thread at exit, after the pump is gone, so awaiting a UI-context continuation would hang | Awaiting it (exit hang); `BackgroundService` (its `ExecuteAsync` runs on the pool, away from the UI context the loops publish on) |
| When the host starts | After the settings are read and the theme applied | The services read the settings on start | Before, relying on `Changed` (works, but starts each loop on defaults first) |
| What the test container starts | The automatic refresh always; the watcher on request | The refresh was running in every test that touched the history; the watcher would watch every repository a test makes | Starting every hosted service; starting none (breaks the refresh tests) |
