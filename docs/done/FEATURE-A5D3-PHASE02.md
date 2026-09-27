# FEATURE-A5D3-PHASE02 — Every page on the helper

**Item:** FEATURE-A5D3 — Info bars that never block
**Branch:** `feature/feature-a5d3-phase02-non-blocking-pages`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Summary

The pages and the shell report through `IInfoBarService.Notify` (PHASE01), so nothing in the
application waits for an info bar any more:

- `ChangesPageViewModel`, `RemotesPageViewModel`, `IdentityPageViewModel`,
  `IntegrationsPageViewModel`, `RepositoriesPageViewModel` and `ConflictResolutionPageViewModel`:
  each `ReportAsync` helper is now a `void Report` forwarding to `Notify`, and its 43 awaited call
  sites are plain calls.
- `MainWindowViewModel`, `SettingsPageViewModel` and `HistoryPageViewModel`: their direct awaited
  `ShowAsync` calls are now `Notify`.

What the user sees:

- A history read that fails ends its busy state at once, so "load more" and the automatic refresh
  work again without the error being closed first.
- A committed change, a resolved conflict, a saved identity, a connected account or a finished clone
  hands control back straight away, and its success message closes itself after 5 seconds.
- Warnings and errors stay until they are closed, as before, and hold nothing up.

Two handlers had awaited nothing but their report, so they are now synchronous:
`MainWindowViewModel.OnNewWindow` and `RepositoriesPageViewModel.OnOpenInNewWindow`. Their commands
are `RelayCommand` / `RelayCommand<RecentRepository>` instead of the async variants.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/MainWindowViewModel.cs` — `NewWindowCommand` is a
  `RelayCommand`
- `src/Enigma.GitClient.App/ViewModels/Pages/` — `ChangesPageViewModel.cs`,
  `ConflictResolutionPageViewModel.cs`, `HistoryPageViewModel.cs`, `IdentityPageViewModel.cs`,
  `IntegrationsPageViewModel.cs`, `RemotesPageViewModel.cs`, `RepositoriesPageViewModel.cs`
  (`OpenInNewWindowCommand` is a `RelayCommand<RecentRepository>`), `SettingsPageViewModel.cs`
- `tests/…/HistoryPageTests.cs` — `Page_AFailedLoadIsOverWhileItsErrorIsStillOpen`, over a
  `FailingCommitLogReader`
- `tests/…/SettingsPageTests.cs` — `Resetting_IsOverWhileItsReportIsStillOpen`
- `tests/…/IdentityPageTests.cs` — the saved-identity success is asserted as timed (5 s)
- `tests/…/InstanceTests.cs` — the five new-window tests run the now-synchronous commands with
  `Execute`
- `docs/roadmap.md`, `docs/plan/FEATURE-A5D3.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A handler whose only `await` was its report | Made synchronous, and its command became a `RelayCommand` | An `async` method with nothing to await is misleading. The .NET 10 compiler no longer warns about it (CS1998), so a script checked every changed file for `async` methods and lambdas without an `await`. Only these two turned up |
| How the history test makes the read fail | A `FailingCommitLogReader` throwing a `GitCommandException` with git's own stderr | Deterministic. Corrupting a real object store would depend on git's version for its message |
| Which page success to assert as timed | Extended the existing saved-identity test | It already asserts the note, and a new test would repeat its setup |
| Proving the tests catch the defect | Ran the history and settings tests against the pre-PHASE02 ViewModels from the run branch, then put the new files back (not committed) | Both failed with `TimeoutException`. With the change they pass |

## Deviations & follow-ups

- None from the plan.
- Line endings: none of this dev's churn is CRLF. The touched files are LF.

## Documentation sweep

No README, `RELEASENOTES.md` or `docs/RELEASE.md` text describes info bars or the two commands, so
nothing was made wrong. The release item writes the 1.1.0 notes.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2227 passed**, 0 failed (2 new, one assertion
  added), with no fix cycle.
- `grep -rn "_infoBar\.ShowAsync\|IInfoBarService>()\.ShowAsync\|ReportAsync\|\.ShowAsync(bar" src`
  finds nothing: no info bar is awaited anywhere in the application.
