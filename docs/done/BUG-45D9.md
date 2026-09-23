# BUG-45D9 — The uncommitted row is shown twice

**Item:** BUG-45D9 — Uncommitted row shown twice
**Branch:** `bugfix/bug-45d9-uncommitted-row-once`
**Run:** bugfix/2026-09-23-uncommitted-row-once

## Summary

The history now shows one "Uncommitted changes" row however its loads overlap, and the page stays
busy until the last running load has finished.

`HistoryPageViewModel.LoadPageAsync` asked git whether anything was uncommitted, added the row, and
only checked whether a newer load had superseded it *after* reading the commits. A newer load cancels
the older one and clears the rows, but cancelling cannot take back an answer git has already given:
when the older load's continuation was still queued on the UI thread, it ran after the newer load's
`Rows.Clear()` and put its row into the newer load's list. Its log read was then cancelled, so the
commits appeared once and the uncommitted row twice. The load now returns, adding nothing, when it has
been superseded while git answered.

The superseded load also cleared `IsBusy` on its way out while the load that superseded it was still
running — and `IsBusy` is what the automatic refresh, Load more and Refresh test before starting yet
another load. Only the load that is still the current one (or one cancelled with nobody taking over,
as when the repository closes) clears it now.

**Why it showed up so reliably:** the page reads the history as it is built — its constructor chooses
the scope, and that setter queues a reload. The repository window builds the page, and the page then
appears while that first read is still running; `OnAppearingAsync` reloads whenever the list is empty,
which it is for the whole of a load. So every repository window started with two overlapping loads.
Once the duplicate was on screen nothing repaired it: the automatic refresh compares "dirty" with
`Rows[0].IsUncommitted`, which the duplicate satisfies.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — `LoadPageAsync`: returns before adding the uncommitted
  row when a newer load has taken over; its `finally` clears `IsBusy` and `_loadCancellation` only
  when no other load owns the page

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — a `GatedWorkingTreeProbe` that answers
  when the test says so (and, like a `git status` that had already finished, answers a load that was
  cancelled meanwhile), a helper that opens the test history over it, and two tests:
  - `Page_ShowsOneUncommittedRowWhenTwoLoadsOverlap` — one uncommitted row, at the top, and the six
    commits once each;
  - `Page_StaysBusyUntilTheLastOverlappingLoadHasFinished` — busy, and Refresh unavailable, between
    the superseded load finishing and the current one finishing; idle afterwards.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the tests get past the page's own first read | Answer it and wait for the page to go idle before starting the test's loads | The constructor's scope setter reads the history once; left unanswered it takes the first answer meant for the test, and the loads under test wait forever. Answering it keeps the test honest about what the page does |
| How a failing test behaves | Every wait bounded (`WaitAsync(Patience)`), and the in-between state read into locals and asserted once both loads are over | A gated probe that is never answered turns a failure into a hang; bounding the waits and not throwing mid-flight makes the red state a red test |
| Which condition clears `IsBusy` | `_loadCancellation is null` or is this load's | `null` covers a load cancelled with no successor (`ReloadAsync` with the repository closed), which would otherwise leave the page busy for good |

## Deviations & follow-ups

- **No deviation from the plan's steps.** One finding beyond it, recorded above: the page's
  constructor starts a load, which is what made the overlap happen at every repository window open.
  It is harmless now; making the constructor not read (or `OnAppearingAsync` not reload while a load
  is in flight) would save one `git status` and one `git log` per window, and is a possible follow-up,
  not a fix this bug needs.
- `RefreshInPlaceAsync` still tests `IsBusy` before its own probe rather than after; with overlapping
  loads now harmless, that window costs at most one redundant reload. Out of scope per the plan.
- **Line endings (recommendation only):** the touched files are LF and the diff has no CRLF churn. No
  action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
    0 Warning(s)
    0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1923  failed: 0  succeeded: 1923  skipped: 0
```

Two tests added. No fix cycle. Against the code before the fix, both new tests fail and for the defect's
own reasons — `Assert.Single() Failure: The collection contained 2 matching items`, and
`Assert.True() Failure` on the page being busy between the two loads.

## Documentation sweep

`RELEASENOTES.md` says the uncommitted changes sit at the top of the graph — still true, now once.
`README.md` does not describe the row. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md`.
No edits.
