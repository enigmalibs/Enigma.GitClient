# FEATURE-5EC4-PHASE02 — The minimap replaces the scrollbar

**Item:** FEATURE-5EC4 — A minimap scrollbar for diffs
**Branch:** `feature/feature-5ec4-phase02-minimap-scrollbar`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

The diff's vertical scrollbar is gone and the change map stands in its place: a strip down the right
of the patch showing where the additions and the removals are over the whole file, with a window
over the rows on screen, which scrolls the patch when it is clicked or dragged.

`Hidden`, not `Disabled`: the map replaces the *bar*, not the scrolling, so the wheel, the keyboard
and the selection still move the list exactly as before.

Each rendering became a two-by-two grid — the list, the map beside it, the sideways bar under it —
rather than a dock. That is not decoration: the sideways bar's width is what tells the view how many
characters of a line fit, so a bar running the full width under the map would be measuring a pane
that is not there. In the grid, the map is exactly as tall as its list and the bar exactly as wide.

Each rendering has its own map, bound to its own rows, so switching between unified and side by side
re-binds nothing — the map that was already describing the rendering on screen simply becomes the
visible one.

## Files / modules touched

**Modified — App**

- `Views/Panels/DiffViewerView.axaml` — each rendering is now a two-by-two grid holding its list,
  its `DiffMinimap` and its horizontal bar; both lists hide their vertical scrollbar; the map hides
  itself when there is no patch
- `Views/Panels/DiffViewerView.axaml.cs` — `Maps` ties a list's scroll viewer to its map in both
  directions: `Report` turns offset, extent and viewport into the two fractions the map draws, and
  `ScrollTo` answers `ScrollRequested` by moving the scroll viewer

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — a "the minimap in the view" section:
  neither rendering shows a vertical scrollbar while both still scroll, the map's window follows the
  patch as it is scrolled and reaches the bottom at the end, a press on the strip scrolls the patch
  and clamps at both ends, each rendering's map describes its own rows and only the one on screen is
  shown, and no map appears when there is no patch. `ShowScrollable` is the shared setup

**Modified — docs**

- `README.md`, `RELEASENOTES.md`, `docs/roadmap.md`, `docs/plan/FEATURE-5EC4.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Hidden or disabled scrollbar | Hidden | Disabled would take the wheel and the keyboard with it; the request replaces the bar, not the scrolling |
| The rendering's layout | A two-by-two grid | The map must be the list's height and the bar the list's width — the bar's width is the character viewport, so a bar under the map would mis-measure it |
| One map or two | One per rendering, each bound to its own rows | Switching rendering then re-binds nothing, and neither map can be showing the other's row indices |
| Where the scroll plumbing lives | The view's code-behind | Which rows are on screen is a fact about a realised, virtualising list; the control takes fractions and the ViewModel never hears about pixels |
| Finding the scroll viewer | `PART_ScrollViewer` on `TemplateApplied` | The same route the history page's header takes, and the name Fluent's `ListBox` template uses |
| The map with no patch | Hidden | A strip beside an empty state says there is something to navigate, and there is not |
| Where the map's marks sit when the patch fits on screen | Spread over the whole strip | The strip is the file, not the viewport: a change a third of the way down the file is drawn a third of the way down the strip whether or not the file is scrolling |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- `Minimap_FollowsThePatchAsItIsScrolled` asserts a range rather than an exact half: a list snaps an
  offset to a row boundary, and a virtualising panel's extent is an estimate that firms up as rows
  are realised, so the two together are never exactly the half that was asked for. The end of the
  patch is asserted exactly, because that one is clamped.
- The snapshots `diff-unified.png` and `diff-side-by-side.png` were inspected: the strip is on the
  right of both renderings, the runs are in the right order and the window covers the rows on
  screen.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1712  failed: 0  succeeded: 1712  skipped: 0
```

Six tests added (two of them a theory's cases). Two fix cycles: `ScrollBarVisibility` needed its
namespace in the test file, and the mid-patch scroll assertion was exact where the list's own
rounding makes it approximate.

## Documentation sweep

- `README.md` — a bullet under the diff features for the minimap that replaced the scrollbar.
- `RELEASENOTES.md` — the same in "The diff" section, saying what it draws and that it scrolls.

Neither document described the diff's scrollbars before, so nothing was made wrong — these are
additions, not corrections. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the
repository.
