# FEATURE-7762 — Drag a branch to merge it

**Status:** DONE — see `docs/done/FEATURE-7762.md`
**Type:** FEATURE
**Branch:** `feature/feature-7762-drag-merge-history`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

In the history, drag one branch badge onto another: a menu opens at the drop and lets the reader
choose **Merge** or **Fast-forward**, both written out in full with the two branch names. The dragged
branch is the source, the branch it is dropped on is the destination.

## Context & constraints

- FEATURE-3030 PHASE03 removed badge dragging from the history. It used the platform drag, which the
  compositor answered badly (BUG-1A42, BUG-11A4). The branches dialog now runs its own gesture
  (`BranchesPageView.axaml.cs`): a tunnelling press/move/release, a pointer capture, a `droptarget`
  ring, auto-scroll near the edges, Escape to cancel, and a `ContextMenu` opened at the pointer. The
  maths live in `BranchDragGesture`.
- The merge itself is `IBranchDropOperations.DropAsync(BranchDropRequest, FastForwardMode)`, already
  injected in `HistoryPageViewModel`; `BranchDropOperations.CanDrop` says what may be dropped where
  (the destination must be a local branch other than the source).
- The app's full-name wording already exists: `Merge "src" into "dst"` and
  `Merge "src" into "dst", fast-forward only` (the branches dialog's drop menu, the badge menu).
- Branch badges are `HistoryBranchViewModel`s rendered as `RefBadge`s in each row's `RefStrip`.

## Steps

1. `HistoryBranchDrop(Source, Target)` record (`HistoryBranchViewModel` pair): its request, its two
   headers, `CanDrop`.
2. `HistoryPageViewModel`: `MergeDropCommand` and `FastForwardDropCommand`, through
   `_dropOperations.DropAsync`, reloading the history after a merge.
3. `HistoryPageView.axaml.cs`: the page-run gesture on branch badges (reusing `BranchDragGesture`):
   press on a branch badge, a move past the threshold starts it, the badge under the pointer that
   would accept the drop is ringed, the list scrolls near its edges, Escape cancels, a release on an
   accepting badge opens the two-item menu at the pointer.
4. `Themes/Controls.axaml`: a `RefBadge.droptarget` ring.
5. Tests (headless, real git): a drop of a feature badge on `main` opens the menu with the two full
   headers; choosing Merge records a merge commit on `main`; Fast-forward moves `main` without one;
   a drop on a remote badge or on itself offers nothing; Escape cancels.

## Acceptance criteria

- Dragging a branch badge onto another local branch badge opens a menu with
  `Merge "<source>" into "<destination>"` and `Merge "<source>" into "<destination>", fast-forward only`.
- Choosing an item performs that merge on the destination, with the usual confirmations and messages.
- Nothing happens on a drop that cannot be merged, and Escape cancels a drag.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Dropping a branch on a commit line (not a branch); rebasing; the reverse merge item the branches
  dialog has.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Drag mechanism | The page-run gesture, as in the branches dialog | The platform drag is what broke before (BUG-11A4) | Avalonia `DragDrop` |
| Menu wording | `Merge "src" into "dst"` / `Merge "src" into "dst", fast-forward only` | Full names both ways, as asked, in the wording the app already uses everywhere | A new "Fast-forward dst to src" phrasing (a second vocabulary for the same action) |
| Menu items | The two the request names | The source/destination roles are fixed by the gesture | Adding the reverse merge |
| Drop targets | Branch badges only | "Drag a branch into another one" | Whole commit lines |
