# FEATURE-1296-PHASE02 — The periodic fetch and refresh

**Item:** FEATURE-1296 — Automatic fetch and refresh
**Branch:** `feature/feature-1296-phase02-auto-refresh`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

While a repository is open, it is now fetched and refreshed on its own, every 15 seconds by default
and at whatever interval Settings names (0 turns it off). The interval follows the setting live.

**`IAutoRefreshService`** owns a `PeriodicTimer` on an injected `TimeProvider`. The timer is started
when a repository opens and stopped when it closes, restarted when the interval changes, and stopped
at 0. Each tick:

1. **Fetches quietly** (`ISyncOperations.FetchQuietlyAsync`): every remote, pruned, with tags. There is
   no overlay and no info bar, whatever happens; a failure is logged at debug level. The fetch goes
   through the new `IRepositoryContext.TryRunExclusiveAsync`, which runs only if no other write holds
   the repository. A tick that finds the reader busy does nothing rather than wait behind them.
2. **Refreshes the reference state**, inside the fetch on success. When the fetch failed (offline, say),
   the refresh runs separately, because a commit made in a terminal is still worth seeing.
3. **Says whether anything moved**, by comparing a `RepositoryStateStamp` taken before and after.

A tick never overlaps another. What follows the context's `StateRefreshed` updates on its own: the
branches, the tags, the Changes page (which already re-read its status on every refresh) and the
strip's ahead/behind counters.

**The history** is told about every refresh by the repository window's ViewModel.
`RefreshInPlaceAsync` does **nothing** when no reference moved and the uncommitted-work row is still
right, which is the common case. Otherwise it reads again as many commits as were loaded, so
"Load more" is not undone. It re-selects the same commit by SHA and raises
`RowsReplacing`/`RowsReplaced` so the view can put its scroll offset back. While the diffs have the
page, the redraw waits for them to close.

## Files / modules touched

**Added — App**

- `Services/AutoRefreshService.cs` — `AutoRefreshResult`, `IAutoRefreshService`, `AutoRefreshService`

**Modified — App**

- `Services/IRepositoryContext.cs`, `Services/RepositoryContext.cs` — `TryRunExclusiveAsync`
- `Services/SyncOperations.cs` — `FetchQuietlyAsync`, `QuietFetchResult`
- `ViewModels/Pages/HistoryPageViewModel.cs` — `RefreshInPlaceAsync`, `RowsReplacing`, `RowsReplaced`,
  the deferred redraw while the diffs are open
- `Views/Pages/HistoryPageView.axaml.cs` — keeps the list's scroll offset across a replace
- `ViewModels/MainWindowViewModel.cs` — hands each refresh to the history
- `DependencyInjection/ServiceCollectionExtensions.cs` — `TimeProvider.System` and the service

**Tests**

- `AutoRefreshTests.cs` (new) — it runs only while a repository is open; it ticks at the interval, not
  before; 0 stops it and a new interval restarts it; a refresh says whether a reference moved; a busy
  repository is skipped silently; a failed fetch still reads the local state and tells nobody; two
  refreshes never overlap; the real quiet fetch fails without a word and never waits behind the
  reader's work; the history stays exactly as it was when nothing moved, redraws and keeps the
  selected commit when something did, notices uncommitted work appearing, and waits for the diffs to
  close; the repository window hands each refresh to the history
- `Infrastructure/ManualTimeProvider.cs` (new) — a clock that moves only when a test moves it
- `Infrastructure/TestServices.cs` — every test container runs on the manual clock, so the refresh can
  never fire in the middle of an unrelated test

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Who keeps the service alive and connects it | The repository window's ViewModel, which already connects the history to the shell | The history's constructor already takes 16 dependencies; the shell is where page-to-page wiring lives |
| The Changes page | No change | It already re-reads its status on every `StateRefreshed`, so the tick's refresh is enough (plan step 5 was already true) |
| A failed fetch | Refresh the local state anyway, log at debug | Local changes are still worth seeing; a warning every 15 seconds offline is noise |
| Keeping "Load more" | One read of as many commits as were loaded | Otherwise every refresh that found something would cut the list back to one page |
| The scroll offset | Restored by the view at `Loaded` priority | The new rows must be measured before an offset into them means anything |

## Deviations & follow-ups

- Plan step 5 (the Changes page) needed no code; see above.
- One fix cycle: inside a `TimeProvider` subclass, `System.Threading…` resolves against the inherited
  static `TimeProvider.System` property. The test clock uses a using directive instead.
- Follow-ups, out of scope per the plan: pause while the window is inactive; back off while the fetch
  keeps failing; mention the automatic refresh in the README's features and the release notes'
  preferences list. Those lists are incomplete rather than wrong, so the sweep left them.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1921 passed, 0 failed (14 new).
