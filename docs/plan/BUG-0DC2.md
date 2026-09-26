# BUG-0DC2 — Scrolled diff text covers the gutter

**Status:** DONE — see `docs/done/BUG-0DC2.md`
**Type:** BUG
**Branch:** `bugfix/bug-0dc2-clip-scrolled-line`
**Run:** bugfix/2026-09-17-diff-gutter-overlap

## Objective

Keep a horizontally scrolled diff line inside its own text column, so it stops painting over the
line numbers and the marker to its left.

## Context & constraints

- Reported as "when I scroll horizontally the left or right side, the content scrolls well but goes
  over/under (hard to say if it's over or under) the lines numbers on its left side".
- The cause is in `Controls/Diff/DiffLineText.cs`: `Render` draws at `origin = (-ScrolledPixels(),
  0)`. The offset is render-only on purpose — that is what keeps the gutter still while a pane
  scrolls — but nothing stops the glyphs that move left of the control's own origin from being
  painted there.
- `BUG-1D34-PHASE02` put `ClipToBounds` on each **pane `Border`** of
  `Views/Panels/DiffViewerView.axaml`. That border holds the gutter, the marker *and* the text, so
  "inside the pane" was never "inside the text column": a line shifted left is still inside the
  border, and is drawn over the numbers. That clip is still needed — it is what keeps a long line
  out of the pane beside it — so this item adds containment rather than moving it.
- "Over or under" is settled by z-order: the `DiffLineText` is the last child of the row grid, so it
  paints **over** the gutter background, the selection bar and the numbers.
- The same control is drawn by `Views/Pages/ConflictResolutionPageView.axaml` in a three-way row
  with no clip anywhere in between, so a long "ours" line paints over the "base" pane today.
- The control also measures to its natural width, so it overflows to the right as well; both edges
  are the same missing containment.

## Steps

1. `Controls/Diff/DiffLineText.cs`: default `ClipToBounds` to `true` for the control
   (`ClipToBoundsProperty.OverrideDefaultValue<DiffLineText>(true)` in the static constructor), with
   a remark on the type saying why a control that draws at a negative origin owns its own ink.
2. `tests/.../Infrastructure/SnapshotColours.cs`: a `Count(Stream, PixelRect)` overload that counts
   the distinct colours of one region of a frame, with the existing whole-frame overload delegating
   to it — the assertion "nothing was painted *here*" needs a region, not a frame.
3. `tests/.../DiffViewerTests.cs`: a control-level render test — a scrolled line beside a stand-in
   gutter strip, the strip asserted to be one flat colour while the text column is not — and a
   view-level test that the real template's line control clips and is laid out to the right of its
   gutter.

## Acceptance criteria

- A `DiffLineText` at a non-zero `HorizontalOffset`, laid out beside a filled strip, leaves that
  strip a single colour; the same frame's text column holds more than one, so the line really did
  draw. The test fails on the current control.
- A freshly constructed `DiffLineText` reports `ClipToBounds` true, so no template has to remember
  it.
- In a shown `DiffViewerView`, each pane's `DiffLineText` clips and its bounds start to the right of
  the gutter and marker widths `DiffTypography` reports.
- The pane borders keep their own `ClipToBounds` — the `BUG-1D34` overlap tests stay green.
- Scrolling still moves the text and leaves the numbers and the marker where they are, and a wrapped
  line is unaffected.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- Horizontal scrollbars for the conflict-resolution page's three-way view, still out of scope as it
  was in `BUG-1D34`; this item only stops its lines painting over each other.
- Synchronising the two panes' offsets, and any change to the scroll arithmetic itself.
- Fading or shadowing the gutter edge to signal scrolled-away text — decoration nobody asked for.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the clip belongs | A default value on `DiffLineText` itself | The control draws outside its bounds by construction — negative origin when scrolled, natural width always — so containing its own ink is its invariant, not something three call sites must remember | `ClipToBounds="True"` on each template instance (forgettable, and misses the conflict page); a setter in `Styles.axaml` (lost wherever the theme is not applied, including the bare-control tests) |
| The conflict page inheriting it | Yes, deliberately | It has the very overlap `BUG-1D34` fixed for the viewer — a long "ours" line over the "base" pane — and containment is better than painting over the neighbour | scoping the clip to the viewer's three instances |
| The pane borders' existing clip | Kept | It contains the row tint and the gutter, and it is what holds a long line out of the pane beside it; the two clips answer different questions | replacing it with the control's clip |
| How it is proved | A pixel-region assertion: the strip beside a scrolled line stays one colour | It asserts the reported symptom rather than the property that happens to fix it, and it fails on today's control | asserting `ClipToBounds` alone; a whole-window snapshot (fragile, and the gutter is a few pixels of it) |
| A half-glyph at the clip edge | Accepted | Exactly what a code editor shows at the left of a scrolled view; rounding the offset to whole characters would make the bar move in jumps | rounding the offset to a character boundary |
