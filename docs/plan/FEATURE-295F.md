# FEATURE-295F — Diff viewer: sync, paths, full file

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-18-history-and-diffs

## Objective

Make the side-by-side rendering read like two views of one file: the two sides scroll together, the
whole file is shown rather than the changed parts alone, and the file list beside it shows names
rather than paths.

## Context & constraints

- **Vertical synchronisation already exists and is structural.** One `ListBox` holds
  `SideBySideRows`, and each row carries both sides (`DiffRowViewModel.Left` / `.Right`), with
  filler cells inserted by `DiffRowBuilder.BuildSideBySide` where one side has no line. The two
  sides therefore cannot drift vertically — there is one scroll. This phase's work is the horizontal
  axis, and the vertical claim is a thing to assert, not to build.
- **Horizontally the two panes are independent today.** `DiffRenderOptions` carries three
  `DiffScrollState`s — `UnifiedScroll`, `LeftScroll`, `RightScroll` — each with its own `Columns`
  (the widest line of that side), `Viewport` (reported by the view, in characters) and `Offset`. The
  view draws one `ScrollBar` per pane, and `DiffViewerView.PaneUnder` routes a sideways wheel to
  whichever half the pointer is over. Everything is counted in characters because the diff is
  monospace by construction.
- The existing tests state the independence as a contract — "the left pane is scrollable and the
  right one is not", "moving the left offset leaves the right at 0" — so making the panes share a
  scroll is a deliberate change of that contract, and those tests change with it.
- **Whole-file context already exists**: `DiffViewerViewModel.WholeFileContext` (100 000) is the
  context-line count that means "the whole file", `ExpandAllContextCommand` already asks for it, and
  `ContextLines` is what `ReloadAsync` hands git. So "show the full file side by side" is a question
  of *which* context the reload asks for, not of a new read path.
- `DiffParseOptions.Default.MaxLinesPerFile` truncates very large patches, and whole-file context
  reaches that limit far sooner than three lines of context do. The viewer already has the answer:
  `IsTruncated` shows a band with a `Show anyway` button that re-reads without the limit.
- The unified rendering is not in the request and keeps the reader's `DiffContextLines` preference
  and its expand-context buttons.
- In the flat list, a row is built as `new ChangedFileNodeViewModel(this, file, file.Name,
  showDirectory: true)`, and that flag is the only thing `ShowDirectory` / `HasDirectoryLabel` /
  `DirectoryLabel` exist for. The row's name already carries `ToolTip.Tip="{Binding Path}"`, so the
  full path stays one hover away once the column is gone.

## PHASE01 — Synchronised side-by-side scrolling

**Branch:** `feature/feature-295f-phase01-synced-scroll`
**Status:** TODO

### Steps

1. `ViewModels/Panels/DiffViewerViewModel.cs`: `LeftScroll` and `RightScroll` become one
   `SideBySideScroll`. `Panes()` yields it and `UnifiedScroll`; `WrapLines` disables both as before.
2. Same file, `MeasureExtents`: the shared state's `Columns` is the **wider** of the two sides, so
   either pane can be scrolled to the end of the longest line on either of them and the two bars
   always agree about how far there is to go.
3. `Views/Panels/DiffViewerView.axaml`: both `DiffLineText`s bind
   `HorizontalOffset="{Binding Options.SideBySideScroll.Offset}"`, and both `ScrollBar`s bind the
   one state. Two bars rather than one: the request asks for the left and right scrollbars to be
   synchronised, and each bar stays under the pane it belongs to.
4. `Views/Panels/DiffViewerView.axaml.cs`: `ReportViewports` gives the shared state the **smaller**
   of the two bars' character counts, so neither pane can be scrolled past its own view;
   `PaneUnder` returns the shared state for both halves of the side-by-side rendering.
5. Tests — `tests/.../DiffViewerTests.cs`: the tests that asserted the panes were independent now
   assert they move together — one offset, one maximum, both bars reflecting it — and one new test
   states the vertical case explicitly: the two sides are cells of the same row, so a side-by-side
   rendering has exactly one vertical scroll for both of them.

### Acceptance criteria

- Moving either bar moves both panes' text by the same number of columns.
- The shared maximum is driven by the wider side, so the longest line on either pane can be read to
  its end.
- The shared viewport is the narrower of the two panes', so neither is scrolled past its own view.
- A sideways wheel over either half moves both.
- Wrapping still turns the bars off, and a newly loaded file still starts at column 0.
- The rows of a side-by-side rendering carry both sides, so one vertical scroll moves both.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The whole file, side by side

**Branch:** `feature/feature-295f-phase02-whole-file`
**Status:** TODO

### Steps

1. `ViewModels/Panels/DiffViewerViewModel.cs`: the context handed to git becomes
   `EffectiveContextLines` — `WholeFileContext` while the side-by-side rendering is shown, the
   reader's `ContextLines` otherwise.
2. Same file: switching `ViewMode` re-reads the patch, because the two renderings now ask git for
   different things. Guarded by the existing generation counter, so a switch while a read is in
   flight discards the older answer.
3. Same file: `ExpandContextCommand` and `ExpandAllContextCommand` cannot execute while the whole
   file is already shown, and the hunk band's click does nothing rather than re-reading identically.
4. `ShowPatch` (the stash's path, which has no target to re-read) is unaffected: it renders what it
   was handed, in whichever mode.
5. Tests — `tests/.../DiffViewerTests.cs`: side by side, a file with one changed line in the middle
   renders every line of the file, with the unchanged ones as context on both sides; switching to
   unified re-reads and shows the context the preference asks for; switching back shows the whole
   file again; the expand commands refuse while side by side; a patch too large for the parse limit
   still offers `Show anyway`.

### Acceptance criteria

- Side by side, a 200-line file with one changed line renders 200 line rows per side.
- Unified keeps the `DiffContextLines` preference — the same file renders its changed line plus the
  configured context and nothing more.
- Switching the rendering re-reads and the switch is not left showing the other mode's rows.
- Both expand-context commands report they cannot execute while the whole file is shown.
- A file past `MaxLinesPerFile` still reports itself truncated and is still shown in full by
  `Show anyway`.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — No path beside the file name

**Branch:** `feature/feature-295f-phase03-no-path-column`
**Status:** TODO

### Steps

1. `ViewModels/Panels/ChangedFilesPanelViewModel.cs`: the flat list builds its rows without the
   directory, and `ShowDirectory`, `DirectoryLabel` and `HasDirectoryLabel` go with it — dead once
   nothing sets the flag.
2. `Views/Panels/ChangedFilesPanelView.axaml`: the dimmed directory column leaves the shared row
   template; the remaining columns close the gap.
3. The name's tooltip keeps showing the whole path, which is what tells two files of the same name
   apart now that the column is gone.
4. Tests — `tests/.../ChangedFilesPanelTests.cs`: the test asserting a list row's `DirectoryLabel`
   asserts instead that a list row shows the bare name and carries its full path for the tooltip and
   for selection; the tree's rows are unchanged.

### Acceptance criteria

- A list row shows the status chip, the name, the rename arrow where there is one, the row action
  and the line counts — and no directory.
- `Path` still carries the full path, so selection, the row menu and the tooltip are unaffected.
- The tree is unchanged.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- One bar spanning both panes instead of two synchronised ones.
- Making whole-file context a preference, or extending it to the unified rendering.
- Reading the untouched parts of a file some other way than through git's context — no blob reads,
  no second code path.
- Syncing the scroll between the viewer and the conflict-resolution page.
- Showing the directory somewhere else in the list row, or as a grouping.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the panes are synchronised | One shared `DiffScrollState` behind both bars | Two bars bound to one state cannot drift, and there is no mirroring code to get wrong | Mirroring two states' offsets (desyncs the moment the two sides' maxima differ, which they do — the sides have different longest lines) |
| The shared extent | The wider side | Otherwise the longer pane's longest line can never be read to its end | The narrower side; the sum |
| The shared viewport | The narrower pane's | Neither pane may be scrolled past what it can show | The wider; the left bar's, arbitrarily |
| One bar or two | Two, sharing the state | The request names the left and right scrollbars; each stays under its pane | A single bar across the bottom |
| How the whole file is shown | The existing whole-file context | The machinery exists, git does the work, and the word-level diff and the filler alignment keep working unchanged | Reading both blobs and pairing the lines here (a second, untested diff path) |
| Whether unified follows | No | The request says "when displaying diffs side by side", and unified is where a reader wants the changes alone | Whole file everywhere (turns every unified diff into a file listing) |
| Switching modes | Re-reads the patch | The two renderings now ask git different questions; rendering stale rows in the new shape would show the wrong context | Keeping both patches in memory (twice the memory for the case that is already the slower one) |
| Very large files | The existing truncation band | Whole-file context reaches the parse limit sooner, and the band with `Show anyway` is already the answer to that | Raising or removing `MaxLinesPerFile` for side by side |
| The directory members | Removed, not left unused | Nothing sets the flag any more, and a property no caller can turn on is dead weight | Keeping them "in case" |
| Telling two same-named files apart | The name's existing path tooltip | The information is still one hover away, which is what the request's "remove their path in the list view" leaves room for | Showing the path on the selected row only (a row that changes shape when selected) |
