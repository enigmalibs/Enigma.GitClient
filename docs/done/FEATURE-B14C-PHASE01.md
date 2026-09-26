# FEATURE-B14C-PHASE01 — A minimap wide enough to grab

**Item:** FEATURE-B14C — A wider minimap that finds the change
**Branch:** `feature/feature-b14c-phase01-wider-minimap`
**Run:** feature/2026-09-20-ui-polish-diff-page

## Summary

The strip beside a patch goes from 14 px to **36 px**, about two and a half times. It is not
decoration: FEATURE-5EC4 PHASE02 hid the patch's vertical scrollbar and made this the only thing
left to drag a long file by, and fourteen pixels is a hairline to aim at with a trackpad.

Widening it alone would have left three thin marks lost in a wide gutter, so the control's
constants became proportions of whatever width it is given:

| Was | Now | At 36 px |
|---|---|---|
| `SidePadding = 2` | a tenth of the width | 3.6 px each side |
| `MinimumMarkHeight = 2` | a twelfth of the width, never below 2 | 3 px |
| outline pen `1` | an eighteenth of the width, never below 1 | 2 px |

So the map scales rather than stretches, and a future width — a preference, a different host — needs
no second edit here. Nothing else about it moved: `StartFor` works in fractions, so where a press
lands is exactly where it landed before.

## Files / modules touched

**Modified — App**

- `Themes/Controls.axaml` — the `DiffMinimap` theme's `Width`, and the comment that says why 36
- `Controls/Diff/DiffMinimap.cs` — `SideInset`, `MarkHeightOfWidth` and `OutlineOfWidth` replace the
  two constants; the marks and the viewport window are drawn against them

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — the realised map beside a patch is 36
  wide, and a pixel test over a rendered strip: the run does not reach the edges, the first three
  columns are track and nothing else (which a two-pixel inset would have failed), and the middle of
  the strip is the mark

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-B14C.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The width | 36 px | The request asked for two to three times 14; 36 is a comfortable trackpad target and still reads as a gutter beside the patch rather than a second pane |
| The inset and the mark heights | Proportions of the width | A constant that suited a 14 px strip is wrong at 36 and would be wrong again at any other width |
| The window's outline | Also proportional, floor of 1 | At 36 px a one-pixel line is the wash's edge, not a frame around where the reader is |
| How the geometry is tested | Pixels, over a rendered strip | A width assertion alone would have passed with the marks still inset by two pixels, which is the thing that would have looked wrong |
| The colour count across the strip | "at least two" rather than exactly two | The run's anti-aliased edge blends a third quantised colour; what the test is about is that the run does not fill the strip |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- The minimap's width is not a preference. If one is ever wanted, the control is ready for it: every
  number it draws with is now derived from its width.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1749  failed: 0  succeeded: 1749  skipped: 0
```

Two tests added (1747 → 1749). Two fix cycles, both in the new tests: `ShowScrollable` needs the
rendering it asks for to be the one on screen, and the quantised colour count across the strip
includes the run's anti-aliased edge.

## Documentation sweep

`README.md` and `RELEASENOTES.md` both describe the minimap by what it does — where the changes are,
where you are, click or drag to go there — and neither states a size, so the diff made nothing in
them wrong. No edits. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
