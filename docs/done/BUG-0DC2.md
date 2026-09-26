# BUG-0DC2 — Scrolled diff text covers the gutter

**Item:** BUG-0DC2 — Scrolled diff text over the gutter
**Branch:** `bugfix/bug-0dc2-clip-scrolled-line`
**Run:** bugfix/2026-09-17-diff-gutter-overlap

## Summary

A horizontally scrolled diff pane painted its text over the line numbers and the marker beside it.
The fix is one default value: `DiffLineText` now clips to its own bounds.

`DiffLineText` draws at `origin = (-ScrolledPixels(), 0)`. That render-only offset is the whole
reason a scrolled pane leaves its gutter still, but it also means the glyphs move left of the
control's own origin — and nothing stopped them being painted there. The clip `BUG-1D34-PHASE02`
added sits on the pane `Border`, which holds the gutter, the marker *and* the text, so "inside the
pane" was never "inside the text column"; and since the line is the last child of the row grid, it
painted **over** the numbers rather than under them.

The containment belongs to the control rather than to the templates that use it: it is the one
control here that draws outside its bounds by construction — past its right edge because it measures
to the line's natural width, past its left one because of the offset. A
`ClipToBoundsProperty.OverrideDefaultValue<DiffLineText>(true)` in the static constructor gives that
to all four places one is drawn, including the conflict-resolution page's three-way view, which had
the same overlap unreported: a long "ours" line was painting over the "base" pane beside it. The
pane borders keep their own clip — it holds the row tint and the gutter, and it is what stops a long
line reaching the pane beside it, which is a different question from where this line's text starts.

Nothing else moved: the offset arithmetic, the scrollbars, the viewport reporting and the wheel
handling are untouched, so a scrolled pane still moves only its text and a wrapped line still
ignores the offset.

## Files / modules touched

**Modified — App**

- `Controls/Diff/DiffLineText.cs` — `ClipToBounds` defaulted to `true` in the static constructor,
  and a remark on the type saying which edges it draws past and why the ancestor's clip could not
  answer for them

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/Infrastructure/SnapshotColours.cs` — a
  `Count(Stream, PixelRect)` overload that counts one region of a frame; the whole-frame overload
  delegates to it
- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — two tests: a pixel-region one that
  reproduces the bug by taking the containment away and then proves the strip beside a scrolled line
  stays untouched, and a view-level one over the real template — the line clips, and its column
  starts after the gutter and marker widths `DiffTypography` reports

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-0DC2.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the clip is declared | `ClipToBoundsProperty.OverrideDefaultValue<DiffLineText>(true)` | A default travels with the control to every template, including the conflict page; a `Styles.axaml` setter would be absent wherever the theme is not applied, the bare-control tests included |
| Proving it | Turning the clip off inside the test, asserting the gutter strip is polluted, then turning it back on | A test that only asserts the clean state passes just as well on a control that draws nothing; this one fails if the fix is reverted *and* if the sample stops overflowing |
| The region overload's shape | `Count(Stream, PixelRect)`, clamped to the frame, with the existing overload delegating | "Was anything painted *here*" is the question a clip is judged on, and a whole-frame count cannot answer it; one counting loop stays one counting loop |
| What the view-level test asserts | The control's clip and where its column starts, not pixels | The pixel proof is already at control level; at view level the question is whether the real row puts the line to the right of the gutter, which is a layout fact |

## Deviations & follow-ups

- **None from the plan.** All six acceptance criteria are covered.
- **Verified in the real view as well.** A headless capture of the side-by-side rendering at
  `LeftScroll.Offset = 60` was taken twice, with and without the containment: without it the old
  side's text starts at the pane's left edge and the line number under it is gone; with it the
  number and the `−` marker are back and the text starts in its own column. The capture was a
  throwaway, not a committed test — the committed pixel assertion is the control-level one.
- **Follow-up.** The conflict-resolution page now contains its lines but still has no way to reach
  what is clipped: it was out of scope in `BUG-1D34` and remains so here. If it is ever asked for,
  it needs its own per-pane scroll arithmetic, and `DiffScrollState` is ready for it.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1639  failed: 0  succeeded: 1639  skipped: 0
```

Two tests are new. No fix cycle was needed: the suite was green on its first run.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`: both describe the diff as colour-coded, word-level and
available unified or side by side, and neither mentions its horizontal scrolling or its gutter, so
nothing this dev changed made either untrue. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository. Nothing edited.
