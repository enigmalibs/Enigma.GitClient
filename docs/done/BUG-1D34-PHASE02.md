# BUG-1D34-PHASE02 — Clip the panes and scroll them

**Item:** BUG-1D34 — Long diff lines overlap the other pane (PHASE02)
**Branch:** `bugfix/bug-1d34-phase02-clip-and-scroll`
**Run:** feature/2026-09-16-diff-panel-layout

## Summary

The reported bug is fixed by one attribute and made usable by everything around it.

The fix itself is `ClipToBounds` on each pane's `Border`. A `DiffLineText` measures to its natural
width and draws from its own origin, so inside the side-by-side row's `*,1,*` grid a line wider than
its half was painted straight across the divider and over the line on the other side. The pane now
contains what it holds, which is the containment the layout always implied.

Clipping alone would only trade an overlap for an unreachable line, so each pane also scrolls. A
`DiffScrollState` — one per pane, living on the shared `DiffRenderOptions` that every row already
holds — carries the extent, the viewport and the offset, all counted in characters, and clamps the
offset back into range whenever any of the three moves. The viewer measures each pane's extent from
its own longest expanded line when a patch arrives and whenever the tab width changes; the view
reports how many characters fit; the reader moves the offset. Three `ScrollBar`s under the rendering
— one for the unified view, one per pane for the side-by-side one — are bound to those states and
are shown only when that pane has something to reach. A sideways wheel, or shift with an ordinary
one, moves the pane under the pointer.

Two things follow from where the offset is applied. The gutter and the marker do not move, because
only the text binds the offset — the same thing a code editor does, and the reason the bars are not
simply the `ListBox`'s own: a list that scrolls horizontally takes its line numbers with it, and in
the side-by-side rendering it would push the right pane off screen entirely. And wrapping turns the
whole mechanism off: `DiffRenderOptions.WrapLines` disables all three panes, which zeroes their
offsets and hides their bars, because a re-flowed line has no overflow to scroll to.

## Files / modules touched

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — the new `DiffScrollState`, the three panes on
  `DiffRenderOptions` with wrapping disabling them, and `MeasureExtents` / `LongestLine` on the
  viewer, called when a patch is applied and when the tab width changes
- `Views/Panels/DiffViewerView.axaml` — `ClipToBounds` on all three pane borders, the three
  `HorizontalOffset` bindings, and the scrollbar row under the two lists
- `Views/Panels/DiffViewerView.axaml.cs` — reports each pane's viewport in characters when a bar
  resizes, when the data context changes and when the typography changes, and handles the sideways
  wheel

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — ten new tests: four over the scroll
  state's arithmetic, and six over the viewer — the per-pane extents, a wider tab re-measuring them,
  the panes moving independently, wrapping putting them away, a new file starting at its beginning,
  and the long line being contained by a laid-out pane

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-1D34.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the three scroll states live | On `DiffRenderOptions` | Every row already holds that one instance, which is exactly the route an offset has to reach thousands of rows by; a second shared object would have been the same mechanism twice |
| Who disables scrolling while wrapping | `DiffRenderOptions.WrapLines` itself | The toolbar's wrap toggle binds straight to the options rather than through the viewer, so anything downstream of the viewer would have missed it |
| Which control's `IsVisible` a bar's policy drives | A `Panel` around the bar | `ScrollBar` writes its own `IsVisible` from its `Visibility` property, so binding the bar's would have been two writers on one value |
| How the viewport reaches the state | Code-behind, from the bar's own width less the gutters | It is a question about a glyph's width and a control's width; a binding cannot answer it, and the ViewModel should not be measuring fonts |
| A wheel notch | Three columns, on the pane under the pointer | What a text editor does, and the pointer is the only thing that says which of two panes the reader means |
| Where the wheel is handled | A tunnelling handler | The lists claim the wheel for their own vertical scroll, so a sideways one has to be taken on the way down |
| Extent measurement | Counted from the rows, plus one column of air | An extent that grew as rows were realised would make the thumb jump under the hand dragging it |

## Deviations & follow-ups

- **None from the plan.** All six acceptance criteria are covered.
- **Follow-up.** The two panes scroll independently, as planned. If a reader ever asks for them to
  move together, the state is already shared through one object and a "link the panes" toggle would
  be a small addition rather than a rework.
- **Follow-up.** The conflict-resolution page draws the same `DiffLineText` and now inherits the
  diff typography, but not the scrollbars — it was explicitly out of scope, and its three-way view
  would need its own pane arithmetic.
- **Observation.** `Layoutable.DesiredSize` is constrained by the space a parent offered, so the
  test asserting "this line really does overflow its pane" measures the text itself rather than
  reading the control's desired size.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1626  failed: 0  succeeded: 1626  skipped: 0
```

Ten tests are new. One fix cycle was needed, in the test rather than the code — see the observation
about `DesiredSize` above.

## Documentation sweep

Scanned the README and the release notes: both describe the diff viewer as colour-coded, word-level
and available unified or side by side, none of which this dev made untrue. Nothing edited.
