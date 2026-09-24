# FEATURE-7232-PHASE02 — One refresh for everything

**Item:** FEATURE-7232 — Toolbars, one refresh, flat file lists
**Branch:** `feature/feature-7232-phase02-single-refresh`
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Summary

The repository toolbar's refresh is now the application's only refresh button — "Refresh everything:
fetch from every remote, then read the repository again". It runs what the automatic refresh runs
every 15 seconds: a quiet fetch from every remote, then HEAD, the references and the status read
again (and, when the fetch fails, the local state read anyway). Because the reader asked, it also:

- redraws the history in place — keeping the reader's place and selection — whether or not anything
  moved (the periodic refresh only redraws when a reference moved or the uncommitted row appeared or
  went);
- re-reads the Integrations page's repository list for the selected account (the periodic refresh
  never calls the hosting API).

The refresh buttons of the history, changes, branches, tags, remotes and integrations pages are gone,
with the `RefreshCommand`s that only those buttons used. Every page follows the repository's state on
its own, as it already did for the automatic refresh.

A press while an automatic refresh is running runs no second fetch; the running refresh publishes
what it found.

## Files / modules touched

**Modified — App**

- `Services/AutoRefreshService.cs` — `AutoRefreshResult.Requested`; `IAutoRefreshService.
  RequestRefreshAsync`; `RefreshNowAsync` and `RequestRefreshAsync` share one private `RefreshAsync`
- `ViewModels/MainWindowViewModel.cs` — takes the refresh service into a field; `RefreshCommand` runs
  `RequestRefreshAsync`; the `Refreshed` handler redraws the history when a reference moved or the
  refresh was requested
- `ViewModels/Pages/IntegrationsPageViewModel.cs` — takes `IAutoRefreshService`; a requested refresh
  re-reads the selected account's repositories; `RefreshCommand` removed
- `ViewModels/Pages/HistoryPageViewModel.cs`, `ChangesPageViewModel.cs`, `BranchesPageViewModel.cs`,
  `TagsPageViewModel.cs`, `RemotesPageViewModel.cs` — `RefreshCommand` removed (their `RefreshAsync` /
  `ReloadAsync` methods stay)
- `Views/MainWindow.axaml` — the refresh's tooltip and automation name say it refreshes everything
- `Views/Pages/HistoryPageView.axaml`, `ChangesPageView.axaml`, `BranchesPageView.axaml`,
  `TagsPageView.axaml`, `RemotesPageView.axaml`, `IntegrationsPageView.axaml` — the refresh button and
  its grid column removed

**Tests**

- `App.UnitTests/AutoRefreshTests.cs` — a requested refresh fetches, re-reads and says it was asked,
  and the periodic one does not; a request while a refresh runs runs no second one; the button redraws
  the history with nothing moved, keeping the selection; the button picks up a commit made in a
  terminal while offline, telling nobody; the integrations page lists again on a requested refresh and
  not on a periodic one
- `App.UnitTests/ShellRenderTests.cs` — no page has a refresh button of its own; the repository strip
  has exactly one, named "Refresh everything"; `AToolbarButtonsIconIsTheToolbarSize` counts the one
  toolbar icon the branches strip now has
- `App.UnitTests/HistoryPageTests.cs`, `TagsPageTests.cs` — moved off the removed commands
  (`LoadMoreCommand` for "nothing to do without a repository", `IsBusy` for the overlapping loads)
- `App.UnitTests/ToolbarButtonLookTests.cs` — finds the refresh by its new automation name

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The API | `RequestRefreshAsync` beside `RefreshNowAsync`, and `Requested` on the result record (defaulting to `false`) | The periodic loop and every existing caller are untouched; the listeners decide what a requested refresh adds |
| Who reacts to "requested" | The listeners: the window's history handler and the integrations page | The service stays about the repository; neither the service nor the window needs to know the integrations page |
| Cancellation of the button's refresh | None passed, as before | The context links its own repository lifetime into every read and fetch, and swallows the cancellation on close |
| The integrations page on a requested refresh | `LoadRepositoriesAsync` when there is an account, exactly what its button did | Same result as the button it replaces; the account list itself is local and does not change behind the page's back |

## Deviations & follow-ups

- None from the plan.
- Follow-up: the `ConflictResolutionPageViewModel.RefreshCommand` has no button either (none was
  ever bound); it was out of this phase's list and is left as it was.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: neither the README nor the release notes mention a refresh button.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1975 passed, 0 failed (13 new), first run.
