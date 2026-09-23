# BUG-45D9 — The uncommitted row is shown twice

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-45d9-uncommitted-row-once`
**Run:** bugfix/2026-09-23-uncommitted-row-once

## Objective

The history shows **one** "Uncommitted changes" row when the working directory has changes, however
the loads that draw it happen to overlap.

## Context & constraints

Reported from the application: with changes in the working directory (the ones the Changes page
lists), the history shows two rows at its top, both "Uncommitted changes", both opening the same
diffs.

- `HistoryPageViewModel.LoadPageAsync` asks `IWorkingTreeProbe` whether anything is uncommitted,
  **adds the uncommitted row**, then reads the page of commits — and only *after* that read checks
  whether a newer load has superseded it. The commits are guarded; the uncommitted row is not.
- A newer load (`ReloadAsync`) cancels the older one and clears the rows. Cancelling cannot take back
  an answer git has already given: when the older load's `git status` has finished and its
  continuation is still queued on the UI thread, it runs after the newer load's `Rows.Clear()` and
  adds its row to the newer load's list. Its log read is then cancelled, so it adds no commits. The
  newer load adds its own row and the commits — two uncommitted rows, one history.
- Overlapping loads are ordinary: the page appearing while a reload is in flight (`OnAppearingAsync`
  reloads whenever `Rows` is empty, which it is for the whole of a reload), a repository change, the
  automatic refresh (`RefreshInPlaceAsync`, fire-and-forget from the repository window), a setting,
  a scope or first-parent change, the toolbar's refresh.
- **Nothing repairs it afterwards.** The automatic refresh compares "dirty" with
  `Rows[0].IsUncommitted`; with the duplicate on screen the two agree, and it never redraws.
- **A second defect widens the window:** a superseded load's `finally` clears `IsBusy` while the load
  that superseded it is still running. `IsBusy` is what `RefreshInPlaceAsync`, the Load more command
  and the Refresh command test before starting another load, so a third can start in the middle of
  the second.
- `IsBusy` on this page is set only by `LoadPageAsync`.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the fix goes | `LoadPageAsync`, the one place every load goes through | Every trigger listed above funnels into it; fixing one caller would leave the others | Serialising the callers (`OnAppearingAsync`, `RefreshInPlaceAsync`, …) one by one |
| How the uncommitted row is guarded | The load returns without adding it when it has been superseded while git answered | The page is single-threaded: a check and the `Rows.Add` that follows it cannot be interleaved, and a newer load always clears the rows right after cancelling. So a row added before the cancel is cleared, and one after it is never added | Reading status and log in parallel and adding everything at the end (a larger restructuring for the same guarantee); de-duplicating the row after the fact (hides the race instead of removing it) |
| Who clears `IsBusy` | Only the load that is still the current one, or one cancelled with no successor (a closed repository) | `IsBusy` then means what its readers assume: a load is running | Keeping the flag as is (the third overlapping load stays possible); a load counter (more state for the same answer) |
| Whether `RefreshInPlaceAsync` changes | No | Once overlapping loads are harmless, its remaining window costs at most one wasted reload, never a wrong screen | Re-checking `IsBusy` after its probe |
| How the race is tested | A gated `IWorkingTreeProbe` that answers when the test says so, ignoring cancellation like a `git status` that had already finished | It makes the interleaving deterministic, and both new tests fail against the current code | A timing-based test against the real probe (flaky by construction) |

## Steps

1. `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`, `LoadPageAsync`:
   - after the probe's answer, return when the load's cancellation has been requested, before any row
     is added, with a comment saying why the check sits there;
   - in `finally`, clear `IsBusy` and `_loadCancellation` only when no other load has taken over.
2. `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`, loading section:
   - a private gated probe whose answers the test releases one by one;
   - two reloads started back to back, the first one's answer released after the second has begun:
     exactly one uncommitted row, at the top, and the commits once;
   - the same interleaving: the page is still busy after the superseded load has finished, and is no
     longer busy once the second has.

## Acceptance criteria

- Two overlapping reloads of a dirty repository leave exactly one uncommitted row, at the top, and
  every commit once.
- `IsBusy` stays `true` until the last running load has finished, and is `false` afterwards.
- A single, non-overlapping reload is unchanged: the row when dirty, none when clean.
- Build clean with zero warnings; the whole suite green, including the two new tests, and both new
  tests fail against the code before the fix.

## Out of scope

- What the uncommitted row shows or does, and the working-directory probe itself.
- `RefreshInPlaceAsync`'s own sequencing, and the automatic refresh.
- The empty-list flash between `Rows.Clear()` and the first rows arriving.
