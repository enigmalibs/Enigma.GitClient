# FEATURE-8970 — Home list: filter and drag visuals

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-14-home-search-force-push

## Objective

On the start window's repository list (`RepositoriesPageView`):

1. **A filter box.** Typing filters the tiles — the ones that do not match disappear — and clearing
   the text brings every tile back. Tiles cannot be reordered while the list is filtered.
2. **A drag that shows what moves.** While a tile is dragged, the whole tile travels under the
   pointer, and where it will land is a small dashed tile instead of the 2-pixel blue line.

## Context & constraints

- **The page:** `Views/Pages/RepositoriesPageView.axaml` — a header (`Auto,*,Auto` grid: title and
  profile picker | nothing | Open, Clone, Create, theme, About), then a `Grid` holding the
  `EmptyState` and a `ScrollViewer Name="RepositoryScroll"` around an
  `ItemsControl Name="RepositoryList"` (default, non-virtualising `StackPanel`). Each tile is a
  `Border` (`CornerRadius 6`, `Padding 8,10,14,10`, `Margin 0,0,0,8`) with a `DotsSixVertical` grip
  (`Drag to reorder`), a folder icon, the name / missing pill / path, and five buttons bound through
  `$parent[ItemsControl].((vm:RepositoriesPageViewModel)DataContext)`.
- **The gesture** (`RepositoriesPageView.axaml.cs`): pointer-driven, not a platform drag (BUG-11A4).
  Tunnelling press/move/release; `BranchDragGesture.IsDrag` threshold; capture on the list; Escape
  calls it off; auto-scroll near the edges on a timer. `RepositoryReorderGesture` holds the pure
  arithmetic (`SlotFor`, `Moves`, `TargetIndex`). The gap is marked by setting `insertbefore` /
  `insertafter` on the row's `ContentPresenter`, which styles show as `Border.dropline`.
- **The ViewModel** (`RepositoriesPageViewModel`): `Repositories` (observable, the stored order),
  `HasRepositories`, `Replace(entries)` after every read/move/forget, `MoveAsync(entry, index)` with
  the index counted in the full stored list.
- **House filter pattern** (Tags, Branches, host dialog): `SearchText` with
  `UpdateSourceTrigger=PropertyChanged`, trimmed, `Contains(…, OrdinalIgnoreCase)`, and a
  `ClearSearchCommand` enabled while there is text.
- **Window minimum width is 760** (rail 88): the header has no room left for a box at that width.
- **Tests:** `RepositoryReorderTests` and `RepositoriesPageTests` (Desktop, headless). Per the
  project's standing instruction, the Desktop suite is written and compiled but **not run** on this
  Windows machine; Core suites are run.

## Shared rules (all phases)

- Build with zero warnings, and read the AVLN (XAML) warning count separately — those are not
  promoted to errors.
- Keep the existing reorder tests green in intent: the `insertbefore` / `insertafter` classes stay
  the gesture's marks on the row's `ContentPresenter`.
- No new dependency, no settings change.

## PHASE01 — Filter the repositories

**Status:** DONE — see `docs/done/FEATURE-8970-PHASE01.md`
**Branch:** `feature/feature-8970-phase01-filter-repositories`

### Steps

1. `RepositoriesPageViewModel`:
   - keep the full stored list in a private `_all`; `Repositories` shows the entries that match;
   - `SearchText` (semi-auto property, `string.Empty` by default) re-applies the filter and notifies
     `ClearSearchCommand`, `IsFiltering`, `CanReorder`, `HasNoMatch`;
   - `Matches(entry, search)` (static, testable): trimmed search empty → true; otherwise name or
     path contains it, ignoring case;
   - `HasRepositories` counts `_all` (the profile has repositories at all); `HasNoMatch` is
     `HasRepositories && Repositories.Count == 0`;
   - `IsFiltering` = the trimmed search is not empty; `CanReorder` = `!IsFiltering`;
   - `ClearSearchCommand` empties the box;
   - `MoveAsync` refuses (does nothing) while filtering: the index would not be one in the stored
     order.
2. `RepositoriesPageView.axaml`:
   - a filter strip above the list, inside the list area, aligned with the tiles (`Margin 16,12,16,0`
     against the scroll viewer's `Padding 16`), visible when `HasRepositories`;
   - `TextBox` bound to `SearchText`, `PlaceholderText="Filter by name or path"`,
     `AutomationProperties.Name="Filter repositories"`, `Escape` bound to `ClearSearchCommand`;
   - a second `EmptyState` (`MagnifyingGlass`, "No repository matches this filter",
     "Clear the filter to see every repository.") visible on `HasNoMatch`;
   - the grip is visible only while `CanReorder`, in a fixed-width column so the tile does not
     shift.
3. `RepositoriesPageView.axaml.cs`: a press starts no drag while the page cannot reorder.
4. Tests (Desktop, compiled):
   - `Matches` theory: empty/whitespace, name, path, case, no match;
   - typing filters the tiles, clearing brings them all back in the stored order;
   - `HasNoMatch` / `HasRepositories` states;
   - while filtering, a drag gesture on a tile starts nothing and moves nothing;
   - `MoveAsync` while filtering leaves the stored order alone;
   - the grip is hidden while filtering.

### Acceptance criteria

- Typing in the filter box hides every tile whose name and path do not contain the text (ignoring
  case and surrounding spaces); clearing the box (or Escape) shows every tile again, in the stored
  order.
- A filter that matches nothing shows "No repository matches this filter", not "No repositories
  yet".
- While the list is filtered no tile can be dragged, the grip is hidden, and the stored order never
  changes.
- Switching profile keeps the text and filters the new list.
- Build clean (zero warnings, zero AVLN warnings); Core suites green; Desktop tests compile.

## PHASE02 — The tile drags, a dashed tile lands

**Status:** DONE — see `docs/done/FEATURE-8970-PHASE02.md`
**Branch:** `feature/feature-8970-phase02-drag-visuals`

### Steps

1. Move the tile's `DataTemplate` into `UserControl.Resources` (`x:Key="RepositoryTile"`) so the
   list and the drag ghost draw the same tile. Give the tile's card `Classes="tile"`.
2. Replace the `dropline` borders with a **dashed placeholder** laid out in the row, before and after
   the card: a `Rectangle` (`Height 40`, `RadiusX/Y 6`, `StrokeThickness 1.5`,
   `StrokeDashArray 4,3`, stroke `ActionAccentBrush`, a faint accent fill, `Margin 0,0,0,8`), hidden
   by default and shown by `ContentPresenter.insertbefore` / `.insertafter`. The rows below make
   room for it.
3. The dragged tile's own row is dimmed (`ContentPresenter.dragged` → card opacity 0.35).
4. **The ghost:** a `Canvas Name="DragLayer"` over the scroll viewer (`IsHitTestVisible=False`,
   `ClipToBounds=True`) holding a `Border Name="DragGhost"` (shadow, opacity 0.92) around a one-item
   `ItemsControl` that uses `RepositoryTile` — its `DataContext` is the page, so the tile's bindings
   resolve as in the list. Shown when the drag starts, at the tile's width and left edge; its top
   follows the pointer, keeping the offset where the tile was grabbed, clamped to the layer.
5. **The slot is computed in the natural geometry** (as if no placeholder were shown): row tops are
   rebuilt from the first row's top and each card's height plus its 8-pixel gap — a pure
   `RepositoryReorderGesture.Stack(firstTop, heights)` — so the rows moving to make room never change
   which slot the pointer is over.
6. Every way a drag ends (drop, Escape, capture lost, detach) hides the ghost, clears `dragged`, and
   takes the placeholder away.
7. Tests (Desktop, compiled): `Stack` arithmetic; during a drag the ghost is visible and carries the
   dragged entry, the original row is marked `dragged`, the target row shows its placeholder; after
   the drop/Escape the ghost is hidden and no row is marked; the existing reorder tests still hold.

### Acceptance criteria

- While a tile is dragged, a copy of the whole tile follows the pointer vertically, aligned with the
  list, and the original is dimmed in place.
- Where the tile would land, a small dashed tile opens a gap in the list; nothing is shown next to
  the tile's own place (a drop there moves nothing).
- Dropping, Escape and losing the pointer leave no ghost, no dimmed row and no placeholder.
- Reordering still stores the same order as before; the filter of PHASE01 still blocks the drag.
- Build clean (zero warnings, zero AVLN warnings); Core suites green; Desktop tests compile.

## Out of scope

- Persisting the filter text, fuzzy or multi-word matching, highlighting the matched text.
- Keyboard reordering, animated row movement.
- The Branches page's drag (a merge gesture, not a reorder).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| What the filter matches | Name or path, trimmed, case-insensitive substring | The house filter rule (Tags, host dialog); the path tells two clones of one name apart | Name only; fuzzy matching |
| Where the box goes | A strip above the tiles, aligned with them, shown when the profile has repositories | The header has no room left at the window's 760-pixel minimum width — a box there would be squeezed to nothing | In the header's free column like Tags/Branches |
| How reordering is blocked | The gesture refuses to start, `MoveAsync` refuses, the grip is hidden | An index in a filtered list is not an index in the stored order; the hidden grip says the tiles cannot move | A disabled-looking grip; reordering within the filtered subset |
| No match | Its own empty state | "No repositories yet" would be false | An empty list with no message |
| Escape | Clears the box (`ClearSearchCommand`) | One key binding; the command is the house pattern | No shortcut |
| How the dragged tile moves | A live copy follows the pointer vertically, aligned with the list; the original is dimmed | The usual sortable-list look; a vertical list keeps the copy inside the list | Following both axes; a bitmap snapshot (not reliable on the headless platform); hiding the original (changes the geometry) |
| Placeholder | A 40-pixel dashed accent tile laid out in the gap; slots computed in the natural geometry | The 8-pixel gap cannot hold a tile; computing slots without the placeholder keeps them steady while the rows move | Drawn over the gap (overlaps the tiles); a full-height placeholder (the request says small) |
| The ghost's tile | The same `DataTemplate`, in a one-item `ItemsControl` whose `DataContext` is the page | One tile definition; its `$parent[ItemsControl]` bindings resolve | A hand-made copy; a `ContentControl` (bindings to the page break) |
