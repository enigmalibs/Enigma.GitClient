# FEATURE-711F-PHASE04 — Reorder repositories by dragging

**Item:** FEATURE-711F — Repository lists per profile
**Branch:** `feature/feature-711f-phase04-drag-reorder`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

A repository row can be dragged to another place in the list. The order is stored per profile and
survives restarts.

- `IRepositoryListStore.MoveAsync(profileId, path, index)` moves an entry to an index, counted in the
  list without it and clamped. An unlisted path changes nothing.
  `RepositoriesPageViewModel.MoveAsync(entry, index)` calls it and shows the result.
- The gesture is run by the page from pointer events, as the branches list's is: no platform drag
  session, for the XWayland refusal-pointer reason in BUG-11A4.
  - A press on a row's body (not on its buttons) past the drag threshold captures the pointer on the
    list and wears the drag-move cursor.
  - A 2-pixel accent line in the gap between two rows shows where the row would land. It is drawn
    over the gap rather than laid out in it, so the rows do not jump.
  - The list scrolls while the pointer is held near its top or bottom edge, and the line follows as
    the rows go by.
  - Release drops the row. Escape or a lost capture calls the drag off, and a click is still a click.
  - A drop in either gap next to the row itself shows no line and moves nothing.
- Each row now starts with a grip icon (`DotsSixVertical`, "Drag to reorder", north-south cursor) as
  the cue.
- `RepositoryReorderGesture` holds the arithmetic (`SlotFor`, `Moves`, `TargetIndex`) as testable
  functions. It reuses `BranchDragGesture.IsDrag` and `ScrollFor` rather than duplicating them.

## Files / modules touched

**Modified — App**

- `Services/RepositoryListStore.cs` — `MoveAsync`
- `ViewModels/Pages/RepositoriesPageViewModel.cs` — `MoveAsync`
- `Views/Pages/RepositoriesPageView.axaml` — the grip, the two drop lines per row, `RepositoryScroll`
  / `RepositoryList` names, the drop-line styles (local to the view, like the branches page's
  `droptarget`)
- `Views/Pages/RepositoriesPageView.axaml.cs` — `RepositoryReorderGesture` and the gesture: press,
  threshold, capture, steer, auto-scroll, release, Escape (listened for on the window), capture lost,
  and cleanup on detach

**Modified / created — tests**

- `RepositoryListStoreTests.cs` — `MoveAsync` as a theory (to the top, down, to the end, up, in place,
  past the end, below zero), an unlisted path, the other profile's order untouched
- new `RepositoryReorderTests.cs` — `SlotFor`, `Moves` and `TargetIndex` as theories; headless drags:
  past the last row (line shown, order stored), above the first, next to itself (no line, no move), a
  press on a row's button (no drag), Escape (no move), and the grip on every row

**Modified — docs**

- `README.md` — the start window's line mentions the drag order
- `docs/roadmap.md`, `docs/plan/FEATURE-711F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where Escape is listened for | The window (`TopLevel`), tunnelling, while the view is attached | A press on a row's body focuses nothing, so the key may be sent to the window itself and never pass through this view |
| How the drop position is expressed | Slots (gaps) turned into an index "in the list without the row" | Dropping in either gap next to the row is visibly a no-op. The store's index is unambiguous |
| The line | A 2 px `ActionAccentBrush` bar in the 8 px gap, as an overlay (negative margin) | No layout shift while dragging. The same accent as the branch drop ring |
| Where the drop styles live | In the view's `UserControl.Styles` | The branches page keeps its `droptarget` style there too |
| The grip's cursor | North-south resize | It says "moves vertically" before the drag starts. The drag itself wears drag-move |
| Reusing the branch gesture helpers | `BranchDragGesture.IsDrag` and `ScrollFor` | Same threshold and edge speed across the app. Renaming that class to something generic would be churn outside this dev |

## Deviations & follow-ups

- **None from the plan.**
- Keyboard reordering stays out of scope, as planned. The rows take no focus today.
- `BranchDragGesture` is now shared by two views. If a third drag appears, a neutral name
  (`PointerDragGesture`) would read better.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2590 passed, 0 failed (+31).
- One fix cycle: two `Assert.False(collection.Contains(...))` were refused by the xUnit analyzer
  (xUnit2017, an error under the house settings) and became `Assert.DoesNotContain`.
