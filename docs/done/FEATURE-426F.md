# FEATURE-426F — A fetch when a repository opens

**Item:** FEATURE-426F — A fetch when a repository opens
**Branch:** `feature/feature-426f-fetch-on-open`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

**Opening a repository now fetches it.** `RepositoryOpener` — the path every "open an existing
repository" takes: the start window's list, its folder picker, the command line and a second instance —
starts the automatic refresh's own quiet refresh (`IAutoRefreshService.RefreshNowAsync`) once the
repository is open and its state read:

- `git fetch --progress --prune --tags --all`, under the non-blocking write lock, then the state read
  again; the repository window redraws the history when the fetch moved something;
- in the background: the opening does not wait for it, so the window and the history appear at once;
- quiet, like the periodic one: no overlay, no info bar, a failure (offline, no credentials) logged only;
- whatever the interval setting, including 0 (automatic refresh off).

Not after create (no remote yet) or clone (just fetched), which open through the context directly.

### The check asked for: do the 15 s refresh and the refresh button fetch?

**Yes, both do — and they run the same fetch.** Read in the code and pinned by the existing tests:

- The periodic tick (`AutoRefreshService.RunAsync` → `RefreshNowAsync`) and the toolbar's refresh
  button (`MainWindowViewModel.OnRefreshAsync` → `RequestRefreshAsync`) both go through
  `AutoRefreshService.RefreshAsync`, which calls `ISyncOperations.FetchQuietlyAsync`:
  `git fetch --progress --prune --tags --all`, then the repository's state is read again.
- Tests: `AutoRefreshTests.ItTicks_AtTheIntervalTheSettingsName` (one quiet fetch per tick),
  `ARequestedRefresh_RunsTheSameFetchAndSaysItWasAskedFor`,
  `TheRefreshButton_FetchesAndRedrawsTheHistoryEvenWhenNothingMoved`.

What can make it look as if they did not fetch:

- **A quiet fetch that fails says nothing** — offline, a credential git cannot get without asking, an
  unreachable remote. It is logged at debug level only. The toolbar's own *Fetch* button (the
  `ArrowsDownUp` one) reports the same failure in an info bar, which is how to see why.
- A refresh is skipped while another operation holds the repository (or while a refresh is already
  running); the next tick runs it.
- Before this item, the first periodic fetch came a whole interval after opening, and none at all with
  the automatic refresh off.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/RepositoryOpener.cs`: `IAutoRefreshService` injected; the
  fetch after opening.
- `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`: three tests.
- `docs/roadmap.md`, `docs/plan/FEATURE-426F.md`: statuses.

**Created**

- `docs/done/FEATURE-426F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Waited for or not | Not: `_ = RefreshNowAsync(CancellationToken.None)` | The window must not wait on the network; closing the repository cancels the fetch through the context's lifetime |
| The service's creation | Now resolved with the opener, at start-up | It only subscribes to the context and the settings; its loop still starts only when a repository opens |

## Deviations & follow-ups

- None from the plan.
- Follow-up suggestion: when the reader presses the refresh button and its fetch fails, say so (the
  periodic one should stay quiet). It would remove the "did it fetch?" doubt.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

Nothing stale: the README does not describe when the client fetches.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- New tests: `OpeningARepository_FetchesItOnce_WithoutWaitingForTheFetch`,
  `OpeningARepository_FetchesItEvenWithTheAutomaticRefreshOff`, `AFolderThatIsNoRepository_FetchesNothing`
  — pass.
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 341 passed, 1 failed, 2 skipped — the racy
  `RepositoryServiceTests.CloneAsync_ClonesALocalRepositoryAndReportsProgress` again (see
  `docs/done/BUG-3D1F.md`); this dev changes no Core code; it passed 5 times out of 5 alone right after.
- Desktop, targeted (`AutoRefreshTests`, `RepositoriesPageTests`, `AppWindowsTests`, `InstanceTests`,
  `CompositionRootTests`, `ApplicationBootstrapTests`, `RemotesAndSyncTests`, `SplashScreenTests`):
  210 total, 150 passed, 0 real failures, 60 teardown refusals. With the verification-only teardown
  change (not committed): 206 passed, 4 teardown `IOException`s, 0 real failures.
- Fix budget: 0 cycles used.
