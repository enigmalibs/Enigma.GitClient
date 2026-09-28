# BUG-6CE0-PHASE02 — A loader at the top of the history

**Item:** BUG-6CE0 — History badges arrive late
**Branch:** `bugfix/bug-6ce0-phase02-history-loader`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

While the history is still reading what it will draw, a thin indeterminate progress bar runs along
the top of the commit list. That covers two things:

- its commits (`IsBusy`);
- just after an opening, the references its badges come from (a context with no `Head` yet).

It disappears once both have been drawn.

- `HistoryPageViewModel.IsLoading` is `IsBusy || (IsRepositoryOpen && RepositoryContext.Head is null)`.
  It is notified from `OnBusyChanged`, `OnRepositoryChanged` and `OnRepositoryStateRefreshed`.
- `HistoryPageView.axaml`: `ProgressBar` `LoadingBar` is 3 px high and not hit-testable. It is laid over
  the top of the `Workspace` panel and has an accessible name.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `IsLoading` and its
  notifications.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the loader.
- `tests/Enigma.GitClient.App.UnitTests/HistoryOpeningTests.cs` — 2 tests:
  - the flag follows the log read and the reference read;
  - the bar is visible while loading, laid over the list at its top, and gone once drawn.
- `docs/roadmap.md`, `docs/plan/BUG-6CE0.md` — statuses (the item is done).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Docked above the list or laid over it | Laid over the top of the list (the `Workspace` panel's last child) | A docked bar that appears and disappears moves every row by its height, so each automatic redraw would make the graph jump. Overlaid, it takes no room. The plan said "takes no height away from the rows when hidden", and this also holds while it shows. |
| Empty state while loading | Unchanged | Not asked for. The loader now says a read is under way. |

## Deviations & follow-ups

- The bar overlays the list instead of being docked under the toolbar (see above). It sits at the top of
  the view, under the column header.
- If the context can never read the references (a broken repository), the loader keeps showing until a
  later refresh manages to. That is an honest "still reading", and nothing else reports that case today.
- Line endings: no CRLF churn.

## Documentation sweep

No user-facing document describes loading. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2365 passed**, 0 failed (2 new), with no fix cycle.
