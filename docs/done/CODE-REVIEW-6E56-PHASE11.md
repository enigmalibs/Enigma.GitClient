# CODE-REVIEW-6E56-PHASE11 — [Medium] SOLID-04 Diff viewer lifetime

## Summary

`DiffViewerViewModel` was registered transient ("per consumer"), but its only consumer, the
singleton history page, took it by constructor. It therefore lived for the session. That hid two
facts:

- the viewer subscribed to the singleton `ISettingsService.Changed` with a lambda;
- it had no `Dispose`.

The changed-files panel did the same. The first consumer that really built one per use would have
leaked every instance and kept applying settings to dead ones.

- `DiffViewerViewModel` and `ChangedFilesPanelViewModel` are `IDisposable`. They subscribe with a
  named `OnSettingsChanged` handler and unsubscribe in `Dispose`.
- `HistoryPageViewModel` takes `Func<DiffViewerViewModel>` and builds its viewer through it. It
  became `IDisposable` and disposes its viewer and its `Files` panel.
- `WorkingTreePanelViewModel` became `IDisposable` and disposes its `Unstaged` and `Staged` lists.
  Both owners are singletons, so the container disposes them when the host goes.
- `Func<DiffViewerViewModel>` is registered as
  `() => ActivatorUtilities.CreateInstance<DiffViewerViewModel>(provider)`. Built outside the
  container's tracking, a viewer is held only by its owner. Resolved from the root provider instead,
  every disposable transient would stay referenced by the container until the end.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Panels/DiffViewerViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Panels/WorkingTreePanelViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/PanelLifetimeTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE11.md`

## Deviations & follow-ups

- The transient `DiffViewerViewModel` registration stays: three Desktop tests resolve a viewer from
  the container directly. Production code builds viewers only through the factory now.
- The history page's own subscriptions (settings, hidden branches, remote tags, the working tree) are
  left alone. The page is a singleton that lives as long as those services, so they are not a leak.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 37 s).
  - Desktop: compiled, **not run**.
- **Behaviour check (throwaway harness outside the repository):**
  - a panel followed the setting (`List`), was disposed, and stayed `List` after the setting
    changed;
  - the factory gives distinct viewers;
  - a container-built viewer followed `Unified`, was disposed, and stayed `Unified`;
  - `ValidateOnBuild: OK`, and `HistoryPageViewModel` and `WorkingTreePanelViewModel` resolve.
- **New tests (Desktop, compiled, not run):** `PanelLifetimeTests` (4):
  - a disposed viewer, and a disposed panel, no longer follow the settings;
  - the working-tree panel disposes both its lists;
  - the viewer factory gives one viewer per call.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the factory builds a viewer | `ActivatorUtilities.CreateInstance` over the root provider | An owner-disposed transient must not be tracked by the root provider, which would keep every one alive | `provider.GetRequiredService<DiffViewerViewModel>` (tracked until the container goes) |
| Who disposes | The owner that built it: the history page and the working-tree panel, themselves disposed by the container | Ownership follows construction | Disposing from the views (a view is rebuilt on every navigation; the ViewModel is not) |
