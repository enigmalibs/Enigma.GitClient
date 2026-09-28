# FEATURE-7762 — Drag a branch to merge it

**Item:** FEATURE-7762 — Drag a branch to merge it
**Branch:** `feature/feature-7762-drag-merge-history`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

In the history, a branch badge can be dragged onto another branch badge. The dragged branch is the
source, and the branch it lands on is the destination. At the drop a menu opens with the two merges,
both written out in full:

- `Merge "<source>" into "<destination>"` records a merge commit, even when a fast-forward would do;
- `Merge "<source>" into "<destination>", fast-forward only` moves the destination without one, and
  refuses when the two have diverged.

This is the wording the app already uses on the branches dialog and in the badge menus.

The gesture is the page's own, like the branches dialog's since BUG-11A4:

- a press on a branch badge followed by a move of 4 px starts it;
- the pointer is captured, and the cursor is the drag cursor over the list;
- the badge that would accept the drop is ringed (`RefBadge.droptarget`);
- the list scrolls while the badge is held near its edge;
- Escape cancels.

A drop on a remote branch, on the branch itself, or anywhere else offers nothing
(`BranchDropOperations.CanDrop`). The merge goes through `IBranchDropOperations.DropAsync`, which checks
out the destination first when it is not the current branch, as it does everywhere. The history reloads
afterwards.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/HistoryDragMergeTests.cs` — 5 tests through the real input
  system over real git:
  - the menu and its full headers and commands;
  - Merge records a merge commit;
  - Fast-forward moves `main`;
  - remote or self offers nothing;
  - the ring, and Escape.

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryBranchViewModel.cs` — the `HistoryBranchDrop` record
  (request, `CanDrop`, the two headers).
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `MergeDropCommand`,
  `FastForwardDropCommand`, `OnDropAsync`.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml.cs` — the gesture, reusing
  `BranchDragGesture`. `DropMenu` and `IsDragging` are exposed to tests. Escape cancels a drag before it
  closes the diffs.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the badge column's comment no longer
  says badges cannot be dragged.
- `src/Enigma.GitClient.App/Themes/Controls.axaml` — the `RefBadge.droptarget` ring.
- `docs/roadmap.md`, `docs/plan/FEATURE-7762.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The ring's colour | The accent, as the merge-source ring and the branches dialog's drop ring | One "this is where it goes" mark app-wide. The ring is the border the badge already draws, so its size does not change. |
| Reusing the branches dialog's gesture code | Reuse `BranchDragGesture` (threshold, cursor, scroll bands); a history-specific handler set | The two lists hit-test different things (rows vs badges), and a shared base class would couple two views for about 150 lines |
| Platform drops on the list | Still refused | FEATURE-3030's contract ("the commit list does not accept drops") is about platform drag-and-drop and still holds |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

The README describes dragging in the branches list and merging from the graph through the merge source.
Both are still true. The release notes describe the new gesture. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors. Two xUnit analyzer errors in the new test
  file (`xUnit2017`) were fixed before the first test run.
- `dotnet test --solution Enigma.GitClient.slnx`: **2406 passed**, 0 failed (5 new).
