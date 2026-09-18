# FEATURE-3030 — History list: columns, menus, search

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-18-columns-selection-minimap

## Objective

Make the history list behave like a list of columns rather than a strip of text: the row menu opens
wherever the pointer is on the line, the columns have a header and can be resized from it, the
badges no longer start a drag, a merge is drawn smaller than a commit, and the search box highlights
what it found instead of hiding everything else.

## Context & constraints

- The list is a virtualised `ListBox` (`Views/Pages/HistoryPageView.axaml`) whose `ItemTemplate` is
  a `Grid` of six columns: the graph cell, the badge strip, the subject, the author, the date and
  the short SHA. The graph cell and the badge strip already take **one width for the whole page**
  (`HistoryPageViewModel.GraphColumnWidth` / `RefColumnWidth`, bound through
  `$parent[ListBox].((vm:HistoryPageViewModel)DataContext)`), because an `Auto` column measured per
  row puts every later column at a different x on every line. Real columns extend that pattern
  rather than replacing it.
- `ColumnDefinition` is not a `StyledElement`, so it inherits no `DataContext`: a `GridLength`
  binding inside the row template has nothing to bind against. The widths therefore stay on the
  **cells** (`Width="…"`, columns `Auto`), which is what the two existing bound columns already do.
- A `DataGrid` would bring a new NuGet package, and with it the loss of the virtualised
  `CommitGraphCell`, the row-height preference and the per-row context menu. Out of scope by the
  run's "no new infrastructure" rule.
- The row `Grid` sets no `Background`, so the pointer falls through everywhere except on a child —
  which is exactly the reported bug: the context menu lives on that `Grid`.
- Branch dragging is three pieces: the handlers in `Views/Pages/HistoryPageView.axaml.cs`
  (`OnPointerPressed` / `OnDragOver` / `OnDragLeave` / `OnDrop` and their helpers), the
  `RefBadgeItem.DragFormat` in-process data format, and `HistoryPageViewModel.DropBranchCommand` /
  `CanDropBranch` / `Request` / the `BranchDrop` record. The work itself is
  `Services/BranchDropOperations.cs`, which stays — the branches page picks it up in FEATURE-3B62.
- `SearchText` writes `CommitLogQuery.MessageFilter` and re-reads the history, debounced by
  `HistoryPageViewModel.SearchDebounce`. The graph is laid out **per page of commits**, so a
  filtered page draws lanes that connect commits which are not adjacent in the real history — the
  "graph makes no sense" in the request. Matching loaded rows in memory costs nothing, so the
  debounce goes with the query filter.
- `CommitLogQuery.IsFiltered` also covers `FirstParentOnly`, so `EmptyMessage`'s "No commit matches
  the current filter" branch stays reachable once the message filter is gone.
- `CommitGraphCell.DrawNode` already draws a merge differently (a ring rather than a disc) and
  `CalculateNodeRadius` already shrinks a node to fit its row and its lane.
- **Baseline:** `Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress` fails on
  the branch this run started from — git 2.55 narrates nothing for a fast local fetch, so the
  progress transcript is empty. It is unrelated to this item; the gate for every phase is the App
  and Core unit suites green and no new failure anywhere.

## PHASE01 — Right-click anywhere on the row

**Branch:** `feature/feature-3030-phase01-row-hit-area`
**Status:** DONE — see `docs/done/FEATURE-3030-PHASE01.md`

### Steps

1. `Views/Pages/HistoryPageView.axaml`: the row template's `Grid` — the one carrying
   `Grid.ContextMenu` — gets `Background="Transparent"`, so the whole line is a hit target rather
   than only the glyphs on it.
2. Check the same grid stretches to the list's width (the `ListBoxItem`'s content alignment), so the
   empty room after the last column belongs to the row too.
3. Tests — `tests/.../HistoryPageTests.cs`: a hit test at a point inside a realised row but between
   two cells lands on a visual whose self-or-ancestors carry the row's `ContextMenu`; the same test
   over a cell still does.

### Acceptance criteria

- A right-click on any horizontal position of a history row opens that row's menu, gaps included.
- The menu still carries the row it was opened on as its command parameter.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — Real columns with a resizable header

**Branch:** `feature/feature-3030-phase02-column-header`
**Status:** DONE — see `docs/done/FEATURE-3030-PHASE02.md`

### Steps

1. New `ViewModels/Pages/HistoryColumnLayout.cs`: the page's column widths, in pixels — `Refs`
   (seeded from the measured badge width), `Author`, `Date` and `Sha`, each with a minimum; the
   graph column keeps its content-driven width and is not resizable, because a narrower graph
   column clips lanes. `Resize(HistoryColumn column, double delta)` clamps to the minimum and to
   what the subject can spare; `Viewport` is the width the list reports.
2. `ViewModels/Pages/HistoryPageViewModel.cs`: exposes `Columns`, keeps feeding the measured badge
   width into it as the seed until the reader resizes that column themselves.
3. `Views/Pages/HistoryPageView.axaml`: a header row above the list — the same six columns, the same
   `ColumnSpacing`, each cell an explicit width from `Columns`, with a `Thumb` grip straddling each
   boundary (negative margin, so it costs no width) and a `SizeWestEast` cursor. Column titles:
   Graph (blank), Refs, Message, Author, Date, Commit.
4. Same file: the row template's subject column becomes the star column with its minimum; the
   author, date and SHA cells take explicit widths from `Columns` exactly as the graph and badge
   cells already do; the list's horizontal scrollbar is disabled, since the subject now absorbs the
   slack instead of pushing the row wider.
5. `Views/Pages/HistoryPageView.axaml.cs`: the grips' `DragDelta` calls `Columns.Resize`; the list's
   `ScrollViewer` reports its viewport width into `Columns.Viewport`, and the header is given
   exactly that width, so the vertical scrollbar cannot push the header out of step with the rows.
6. Tests — `tests/.../HistoryPageTests.cs`: the layout model alone (resize grows and shrinks, stops
   at the minimum, cannot eat the subject's minimum), and a rendered page where each header cell's
   right edge sits at the same x as the matching cell of every row, before and after a grip drag.

### Acceptance criteria

- The list has a header naming every column, and the header's cells line up with the rows' cells to
  within a pixel — with the vertical scrollbar shown and hidden.
- Dragging a grip resizes its column, and the message column takes or gives the difference.
- A column cannot be dragged below its minimum, nor past the message column's minimum.
- A long commit subject ellipsises inside its column; the list never scrolls horizontally.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE03 — No dragging from the history badges

**Branch:** `feature/feature-3030-phase03-no-badge-drag`
**Status:** DONE — see `docs/done/FEATURE-3030-PHASE03.md`

### Steps

1. `Views/Pages/HistoryPageView.axaml.cs`: the pointer, drag-over, drag-leave and drop handlers go,
   with `BadgeAt`, `CanDrop`, `IsDraggable`, `Highlight` and the `_highlighted` field.
2. `Views/Pages/HistoryPageView.axaml`: `DragDrop.AllowDrop` leaves the list and the badge strip
   loses its "Drag a branch onto another to merge it" tooltip.
3. `ViewModels/Pages/HistoryPageViewModel.cs`: `DropBranchCommand`, `OnDropBranchAsync`,
   `CanDropBranch`, `Request`, `IsBranch` and the `BranchDrop` record go; the
   `IBranchDropOperations` constructor dependency goes with them.
4. `ViewModels/Pages/CommitRowViewModel.cs`: `RefBadgeItem.DragFormat` goes — nothing transfers a
   badge any more.
5. `Themes/Controls.axaml`: the `RefBadge` `.droptarget` style goes; the branches page draws its own
   drop mark on a row in FEATURE-3B62 rather than on a badge.
6. `DependencyInjection/ServiceCollectionExtensions.cs`: `IBranchDropOperations` stays registered —
   FEATURE-3B62 is its next caller.
7. Tests — `tests/.../HistoryPageTests.cs`: the dragging section goes, and one test states the new
   contract: the commit list does not accept drops. The drop policy itself is already covered by
   `BranchDropTests.CanDrop_AcceptsOnlyWhatCanBeMerged`.

### Acceptance criteria

- A branch badge in the history cannot be dragged, and the list refuses drops.
- No history type mentions dragging any more; the app builds with no unused member left behind.
- `BranchDropOperations` and its tests are untouched.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE04 — A merge node half the size of a commit

**Branch:** `feature/feature-3030-phase04-smaller-merge-nodes`
**Status:** DONE — see `docs/done/FEATURE-3030-PHASE04.md`

### Steps

1. `Controls/Graph/CommitGraphCell.cs`: a `MergeNodeScale` of 0.5 and a static
   `CalculateMergeNodeRadius(double radius)` that halves the fitted radius and floors it at 1, so a
   merge stays visible at the smallest row height and lane width the preferences allow.
2. Same file, `DrawNode`: a merge row draws its ring at that radius. The HEAD ring keeps its own
   geometry, measured from the node it encircles, so a merge that is HEAD gets a smaller ring too.
3. Tests — `tests/.../CommitGraphCellTests.cs`: the merge radius is half the commit radius at the
   defaults, never below 1 at the extremes, and a rendered diamond still draws both a commit and a
   merge.

### Acceptance criteria

- A merge node is drawn at half a commit node's radius, at every row height and lane width the
  settings offer.
- A commit node's size is unchanged.
- A merge that HEAD points at still gets its ring, around the smaller node.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE05 — The search highlights instead of filtering

**Branch:** `feature/feature-3030-phase05-search-highlight`
**Status:** DONE — see `docs/done/FEATURE-3030-PHASE05.md`

### Steps

1. `ViewModels/Pages/HistoryPageViewModel.cs`: `SearchText` no longer writes
   `CommitLogQuery.MessageFilter` and no longer reloads; it marks the loaded rows. The debounce
   (`SearchDebounce`, `QueueDebouncedReload`, `DebounceAsync`, `_searchDebounce`) goes with the
   query filter — an in-memory match needs no delay.
2. Same file: `MatchCount` and `MatchSummary` ("3 matches" / "no match" / empty when nothing is
   searched for), recomputed when the text changes and when a page is appended, so
   `Load more commits` marks what it brings in.
3. `ViewModels/Pages/CommitRowViewModel.cs`: `IsSearchMatch`, a settable observable — the row is
   formatted once at construction, and a search must not rebuild the list.
4. `Views/Pages/HistoryPageView.axaml`: the row grid takes `Classes.match="{Binding IsSearchMatch}"`
   and the match count sits beside the search box; `Themes/Styles.axaml` gets the `match` tint,
   which must stay readable under the selection brush.
5. Matching is case-insensitive over the subject and the commit body, which is what a reader means
   by "search commit messages".
6. Tests — `tests/.../HistoryPageTests.cs`: the two tests asserting the search filters the rows now
   assert the rows stay and the matching ones are marked; a search matching nothing leaves every row
   on screen and reports no match; clearing the box unmarks everything; `Load more commits` marks
   the rows it adds.

### Acceptance criteria

- Typing in the search box never removes, reorders or reloads a row, so the graph is unchanged.
- Matching rows are visibly marked and counted; a search with no match says so and hides nothing.
- Clearing the box removes every mark.
- A page loaded after the search is marked as it arrives.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- Any column header sort, column reordering, hiding a column, or persisting the widths between
  sessions.
- Resizing the graph column.
- Searching by author, path or date, and jumping between matches.
- Re-querying git for commits that are not loaded — the search marks what the list holds.
- Replacing the list with a `DataGrid`.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to get resizable columns | A header of explicit widths over a shared `HistoryColumnLayout`, with `Thumb` grips | Extends the per-page-width pattern the rows already use, and keeps the virtualised graph cell, the row-height preference and the row menu | `DataGrid` (a new package, and it would cost all three); `GridSplitter` over bound `ColumnDefinition`s (a `ColumnDefinition` inherits no `DataContext`, so the row template cannot bind one) |
| Which column absorbs the slack | The message column | It is the one that benefits from every spare pixel, and it removes the horizontal scroll the request complains about | The last column (a wide, mostly empty SHA column); every column fixed (brings the horizontal scroll back) |
| What a grip resizes | The fixed column it touches, the message column taking the difference | One rule, and the message column is never dragged directly because its width is whatever is left | Each grip resizing the column on its left (meaningless on the flexible column) |
| Keeping the header aligned with the rows | The list reports its scroll viewport width; the header is exactly that wide | Deterministic, and it survives the vertical scrollbar appearing | Padding the header by a guessed scrollbar width; putting the header inside the list as a row |
| Whether the graph column resizes | No | Its width is the lane count; anything narrower clips lanes the reader needs | A resizable graph column with clipping |
| Where the search runs | Over the loaded rows, in memory | Lines must never disappear, so there is nothing to ask git; the graph stays the one git built | Keeping the git query and only styling the result (still reloads, still redraws a partial graph) |
| The debounce | Removed with the query | It exists to avoid a git process per keystroke; a substring match over loaded rows costs nothing | Keeping a delay nobody needs |
| What the search matches | Subject and body, case-insensitively | "Search commit messages" is what the box says, and git's own `--grep` searched both | Subject only; adding author and hash (a different feature) |
| Telling the reader how much matched | A count beside the box | Without it a search that matched nothing is indistinguishable from one that matched everything | Nothing; a next/previous match navigator (a feature of its own) |
| The merge node's floor | 1 px, like the commit node's | The existing fitting rule already guarantees a visible node at the smallest settings; halving must not undo that | Halving unconditionally (a merge disappears at the smallest row height) |
| Where the badge drag went | Deleted from the view and the page ViewModel; the service kept | The branches page needs the same operation two items later, and it already owns the dialogs and the reporting | Deleting `BranchDropOperations` too and rewriting it for the branches page |
