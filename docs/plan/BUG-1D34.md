# BUG-1D34 — Long diff lines overlap the other pane

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-1d34-phase01-offsettable-line`, `bugfix/bug-1d34-phase02-clip-and-scroll`
**Run:** feature/2026-09-16-diff-panel-layout

## Objective

Stop a long line on the left of the side-by-side diff from painting over the line on the right, and
give the reader a way to reach the rest of the line: a horizontal scrollbar per pane.

## Context & constraints

- Reported as "there is a bug where for long lines the left side lines come over the right side
  lines and overlap. Fix that and add horizontal scrollbars."
- The cause is in `Controls/Diff/DiffLineText.cs`: `MeasureOverride` returns the natural
  `FormattedText.Width`, and `Render` draws the whole text at `(0,0)`. Inside the side-by-side row's
  `Grid ColumnDefinitions="*,1,*"` each pane is a `Border` with no clip, so anything wider than the
  pane is drawn straight across the divider and over the other side.
- There is nowhere to scroll to either: the side-by-side `ListBox` sets
  `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, and the unified one inherits the same
  default.
- The viewer is monospace by construction — `DiffLineText` expands tabs to a fixed column grid
  precisely because a proportional face would not line up — so the scroll arithmetic is done in
  character columns and turned into pixels by the character width `FEATURE-7676-PHASE02` measures.
- Enabling the `ListBox`'s own horizontal scrolling is not the fix: it would scroll the line-number
  gutter out of view, and in the side-by-side rendering it would push the right pane off screen
  entirely — which is the one thing that rendering exists to prevent.
- `Options.WrapLines` already re-flows a long line into the available width. When it is on there is
  no overflow, so there is nothing to scroll and no bar to show.

## PHASE01 — An offsettable diff line

**Status:** TODO

### Steps

1. `DiffLineText.HorizontalOffset`: a render-only styled property, in characters, that shifts the
   drawn text (and the word-level highlight geometry with it) left by that many columns. It affects
   render, never measure — the row's height and the gutter must not move when a pane scrolls.
2. `DiffLineText.MeasureCharacterWidth(FontFamily, double)`: the advance width of one character for
   a face and size, cached, so the control turns a column offset into pixels without measuring per
   row. `FEATURE-7676`'s `DiffTypography` is the one caller that keeps the shared metrics; this is
   the primitive both use.
3. `DiffLineText.ExpandedLength(string, int)`: how many columns a line occupies once its tabs are
   expanded, without building the expanded string — what an extent is computed from, for thousands
   of lines, whenever the patch or the tab width changes.

### Acceptance criteria

- `ExpandedLength` agrees with `Expand(...).Expanded.Length` for text with and without tabs, at
  several tab widths, and for the degenerate width 0.
- A `DiffLineText` measures the same width and height whatever its `HorizontalOffset` is.
- Rendering at an offset moves the glyphs: two frames of the same line at offsets 0 and 20 differ.
- `MeasureCharacterWidth` returns a positive width and the same value for the same face and size.
- `dotnet build` clean with zero warnings; the whole suite green.

## PHASE02 — Clip the panes and scroll them

**Status:** TODO

### Steps

1. A `DiffScrollState` beside `DiffRenderOptions` in `ViewModels/Panels/DiffViewerViewModel.cs`:
   `Columns` (the extent, in characters), `Viewport` (in characters, reported by the view),
   `Offset`, a derived `Maximum` and `IsScrollable`, with the offset clamped whenever any of them
   moves and reset when a new patch arrives.
2. Three of them on the viewer — `UnifiedScroll`, `LeftScroll`, `RightScroll` — with their extents
   computed in `Apply(FilePatch?, string)` from the longest expanded line of each side, and
   recomputed when the tab width changes. Wrapping forces every pane's offset to 0 and hides its
   bar.
3. `DiffViewerView.axaml`: every pane `Border` clips to its bounds — the overlap fix — and every
   `DiffLineText` binds its `HorizontalOffset` to the scroll state of the pane it sits in.
4. A scrollbar row under the rendering: one bar in the unified rendering, one per pane in the
   side-by-side one, each bound to its state and hidden when that pane has nothing to scroll.
5. `DiffViewerView.axaml.cs`: report each bar's viewport in characters (from its own width, the
   gutter and marker widths, and the current character width), re-reporting when the bar resizes and
   when the typography changes; and scroll the pane under the pointer on a horizontal or
   shift-modified wheel.

### Acceptance criteria

- In the side-by-side rendering, a line far wider than its pane is drawn inside that pane only: a
  rendered frame of a row whose left line is long and whose right line is short shows the right
  pane's own background where that line's text would otherwise have reached.
- Each pane's scrollbar appears only when that pane's longest line does not fit, and its maximum is
  the number of columns that do not fit.
- Scrolling a pane moves that pane's text and leaves its line numbers and its marker where they are,
  and leaves the other pane where it is.
- Turning wrapping on hides the bars and puts every pane back to offset 0; turning it off brings
  them back.
- Loading another file puts every pane back to offset 0.
- Changing the tab width changes the extent of a line containing tabs.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- Synchronising the two panes' horizontal offsets — they are independent, which is what a reader
  comparing two differently indented files needs.
- A horizontal scrollbar on the conflict-resolution page's three-way view.
- Keeping the gutter pinned in a *vertical* sense (sticky hunk headers), which nobody asked for.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How a pane scrolls | A render-only offset on `DiffLineText`, driven by a per-pane state | Costs no layout pass, leaves the gutter pinned the way an editor does, and works identically in both renderings | enabling the `ListBox`'s horizontal scrolling (scrolls the gutter away, pushes the right pane off screen); a `ScrollViewer` per cell; a `RenderTransform` per row |
| The unit the scroll works in | Character columns | The viewer is monospace by construction, so a column is the natural step; it also keeps the ViewModel free of font metrics | pixels computed in the ViewModel |
| One bar or two | One per pane, independent | Side by side exists to keep both halves in view, and two files rarely need the same offset | a single bar driving both panes |
| How the extent is found | The longest expanded line, counted rather than measured | A patch is thousands of lines; counting columns is cheap and exact for a monospace face, and a measured extent that grows as rows realise makes the thumb jump | measuring every line; growing the extent from realised rows |
| Wrapping | Hides the bars and forces offset 0 | Wrapped text has no overflow, so a bar would be a control that does nothing | leaving a dead bar visible |
| Where the overlap is actually fixed | `ClipToBounds` on the pane border | It is the containment the layout always implied, and it holds whatever the offset is | clipping inside the control, which would leave every other overflow unclipped |
