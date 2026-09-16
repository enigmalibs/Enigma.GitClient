# BUG-1D34-PHASE01 — An offsettable diff line

**Item:** BUG-1D34 — Long diff lines overlap the other pane (PHASE01)
**Branch:** `bugfix/bug-1d34-phase01-offsettable-line`
**Run:** feature/2026-09-16-diff-panel-layout

## Summary

`DiffLineText` can now be told how far its pane is scrolled, and can say how wide a line is without
building it. Neither is visible yet — the next phase wires them to a scrollbar — but together they
are what makes a pane scrollable without the row around it moving.

`HorizontalOffset` is a styled property counted in *characters*, and it affects render only. That
choice is the whole point: the text moves, the measure does not, so the line numbers and the marker
beside it stay exactly where they are, the row keeps its height, and scrolling costs a repaint
rather than a layout pass over a patch. The word-level highlight geometry is built at the same
scrolled origin, so a tint travels with the words it sits behind instead of staying where they used
to be. While `WrapLines` is on the offset is ignored: wrapped text has no overflow to scroll to, and
honouring a leftover offset would push a perfectly visible line out of view.

`ExpandedLength` answers how many columns a line occupies once its tabs are expanded, running
`Expand`'s own arithmetic without its `StringBuilder` or its index map. A pane's scroll extent is
the longest line of thousands, recomputed whenever the patch or the tab width changes; building
every expansion to read its `Length` would allocate the whole patch again to learn one number.

The plan gave this phase a third step — a cached `MeasureCharacterWidth` on the control.
`FEATURE-7676-PHASE02` needed that primitive first and it lives on `DiffTypography`, so the control
consumes it rather than growing a second copy.

## Files / modules touched

**Modified — App**

- `Controls/Diff/DiffLineText.cs` — the `HorizontalOffset` styled property, its render-time
  application to both the text and the highlight geometry, `ExpandedLength`, and an
  `EffectiveFontSize` helper that the measure and the offset now share

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — four new tests: the column count
  against the expansion it mirrors, the measure holding still under an offset, the glyphs moving out
  of view and back, and a wrapped line ignoring the offset
- `tests/Enigma.GitClient.App.UnitTests/Infrastructure/SnapshotColours.cs` — a `Count(Stream)`
  overload, so one control rendered on its own can be checked the way a window snapshot already was

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-1D34.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where `MeasureCharacterWidth` lives | `DiffTypography`, not this control | The previous dev needed it first; one cached measurement serves the metrics and the control alike |
| What the offset does to measure | Nothing at all | A scrolling pane that re-measures moves its own gutter and every row of the patch with it |
| A leftover offset while wrapping | Ignored by the control itself | It keeps the control correct on its own rather than only inside a ViewModel that happens to reset it |
| How "the glyphs moved" is asserted | Colour count of the control rendered alone | It is the assertion the snapshot helper already makes, and it distinguishes "drew nothing" from "drew elsewhere" without pinning pixel coordinates |
| The highlight geometry | Built at the scrolled origin | A tint left behind at the unscrolled position is worse than no tint at all |

## Deviations & follow-ups

- **Deviation from the plan, step 2.** `MeasureCharacterWidth` is on `DiffTypography`, for the
  reason above. The acceptance criterion it carried — a positive, stable width per face and size —
  is covered by `DiffTypographyTests`.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1616  failed: 0  succeeded: 1616  skipped: 0
```

Eleven tests are new (the column count is a theory of eight cases). One fix cycle was needed, in the
test rather than the control: `Bitmap.Save(Stream)` is obsolete in Avalonia 12 and the build refuses
an obsolete call, so the encoder-options overload is used.

## Documentation sweep

Nothing to sweep: this phase adds no user-visible behaviour — the property it introduces has no
control bound to it until the next one. The README and the release notes describe the diff viewer,
not its internals.
