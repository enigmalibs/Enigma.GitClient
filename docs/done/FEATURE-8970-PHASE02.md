# FEATURE-8970-PHASE02 — The tile drags, a dashed tile lands

**Item:** FEATURE-8970 — Home list: filter and drag visuals
**Phase:** PHASE02 — The tile drags, a dashed tile lands
**Branch:** `feature/feature-8970-phase02-drag-visuals`
**Run:** feature/2026-10-14-home-search-force-push

## Summary

Reordering the start window's repositories now shows what moves and where it lands.

- **The tile travels with the pointer.**
  - Once the drag starts, a copy of the whole tile is drawn on a layer over the list
    (`DragLayer`, not hit-testable, clipped to the list).
  - It is as wide as the row and sits at the row's left. Its top follows the pointer up and down,
    held where the tile was grabbed and kept inside the layer (`RepositoryReorderGesture.GhostTop`).
  - It carries a shadow (`ItemsControl.ghost Border.tile`), at 0.92 opacity.
- **The original tile stays in its place, faded** (`ContentPresenter.dragged Border.tile`, opacity
  0.35).
- **Where it lands is a small dashed tile.**
  - It replaces the 2-pixel line: 40 pixels high, a 1.5-pixel dashed accent outline over a faint
    accent fill, corners like the tiles'.
  - It is laid out in the gap, so the rows below make room for it.
  - It is shown by the existing `insertbefore` / `insertafter` marks on the row's
    `ContentPresenter`. Nothing opens next to the tile's own place, where a drop moves nothing.
- **The gap does not jump when the rows move.**
  - The slot is found among the rows as they stand with no dashed tile open
    (`RepositoryReorderGesture.Stack`): the first row's top, then each tile's height and the 8 pixels
    below it.
  - The rows making room therefore never change which gap the pointer is over.
- **One tile definition.** The tile's `DataTemplate` moved to `UserControl.Resources`
  (`RepositoryTile`). The list and the copy are both `ItemsControl`s whose `DataContext` is the page,
  so the tile's `$parent[ItemsControl]` bindings resolve in both.
- **Every way a drag ends** (drop, Escape, capture lost, the view detached) goes through
  `StopDragging`, which now also hides and empties the copy and un-fades the row.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`: the shared `RepositoryTile`
    template (with a `tile` class and the two dashed placeholders); placeholder, faded-row and
    shadow styles instead of the `dropline` ones; the `DragLayer` canvas with the `DragGhost` copy.
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml.cs`:
    - `RepositoryReorderGesture.Stack` and `GhostTop`;
    - `Lift` and `MoveTheGhost`;
    - `SlotAt` in the unopened geometry (`UnopenedHeight`, `Tile`);
    - `StopDragging` tears the copy down;
    - `ShowTheLine` renamed `OpenTheGap`.
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryReorderTests.cs`: new tests (below).

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| How the copy's shadow follows the tile | A style on `ItemsControl.ghost Border.tile` | The tile's 8-pixel bottom margin would put a shadow drawn on a wrapper below the tile | A shadowed wrapper border |
| How a row's unopened height is read | Its `tile` border's height plus margins, falling back to the row | Independent of whether layout has run since a dashed tile opened | Subtracting the open tile's height (wrong when layout has not run yet) |
| Where the copy is shown | Set `ItemsSource` only while dragging | Between drags the page holds no second set of the tile's buttons for tests or automation to find | A permanently realised, hidden copy |
| The private `ShowTheLine` | Renamed `OpenTheGap` | There is no line any more | Keeping a misleading name |

## Deviations & follow-ups

- No deviation from the plan.
- **Follow-up:** the README and the release notes do not describe the drag's look; nothing they say
  became wrong (the README says only that the rows are dragged into order).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors** (AVLN included).
- Core suites: `Enigma.GitClient.Core.UnitTests` **1173 passed, 1 skipped, 0 failed**;
  `Enigma.GitClient.Core.IntegrationTests` **359 passed, 2 skipped, 0 failed** (3 min 32 s).
- Desktop tests added to `RepositoryReorderTests`, **compiled, not run**: the project's standing
  instruction is never to run the Desktop suite on this Windows machine. Run them on Linux before
  tagging.
  - Unit tests:
    - `Stack` (rows laid out, empty);
    - `GhostTop` theory (held at the grab, clamped at both ends, taller than the layer).
  - Gesture tests:
    - The copy appears with the dragged entry at the row's width, follows the pointer 1:1, and is
      gone after the drop.
    - The row is faded during the drag and not after it.
    - The dashed tile opens below the last row and takes room; Escape closes it.
    - The rows making room do not move the gap, and the drop still puts the row first.
  - The existing reorder tests keep their assertions on the `insertbefore` / `insertafter` marks.
- **Re-run on Linux** when the run was committed: `dotnet build` **0 warnings, 0 errors**;
  `Enigma.GitClient.Desktop.UnitTests` **1519 passed, 0 failed**; `Enigma.GitClient.Core.UnitTests`
  **1173 passed, 1 skipped**; `Enigma.GitClient.Core.IntegrationTests` **361 passed**.
  - The first Desktop run had one failure,
    `RepositoriesPageTests.ARepositoryOpenedWhileAProfileIsPicked_JoinsThatProfilesList`, which this
    phase does not touch (a ViewModel and profile-store test). It passed on a full re-run and three
    runs on its own: an intermittent test to look at, not a regression of this phase.
