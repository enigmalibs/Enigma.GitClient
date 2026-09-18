# FEATURE-295F-PHASE01 — Synchronised side-by-side scrolling

**Item:** FEATURE-295F — Diff viewer: sync, paths, full file
**Branch:** `feature/feature-295f-phase01-synced-scroll`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

The two sides of a side-by-side diff now scroll sideways together. `LeftScroll` and `RightScroll`
became one `SideBySideScroll`, which both bars and both columns of text bind.

One state rather than two kept in step, and deliberately so: mirroring two offsets works only while
the two sides agree about how far there is to go, and they do not — the old file and the new one
have different longest lines, so a mirrored pair desyncs the moment the shorter side clamps. With
one state there is nothing to keep in step.

Its extent is the **wider** of the two sides, so either pane can be scrolled to the end of the
longest line on either of them; its viewport is the **narrower** of the two bars' character counts,
so neither pane is ever scrolled past what it can itself show. In practice the two are equal — the
panes are a 50/50 split — but the smaller is the honest answer when they are not.

`PaneUnder` no longer asks which half the pointer is over: the side-by-side rendering has one scroll,
so a sideways wheel anywhere in it moves both sides, which is what synchronised means.

**Vertically nothing needed building, and this phase says so with a test instead.** The two sides are
cells of one `DiffRowViewModel` in one `ListBox`, with filler cells where a side has no line, so they
cannot drift — there has only ever been one vertical scroll for both. `Viewer_HasOneVerticalScrollForBothSides`
now pins that, so a future change that split the rendering into two lists would fail rather than
quietly reintroduce the problem this phase was asked to solve.

## Files / modules touched

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — `LeftScroll`/`RightScroll` → `SideBySideScroll`;
  `Panes()` yields two; `MeasureExtents` takes the wider side; the wrap toggle disables both states
- `Views/Panels/DiffViewerView.axaml` — both `DiffLineText`s and both `ScrollBar`s bind the one
  state, and the bars announce themselves as scrolling both files
- `Views/Panels/DiffViewerView.axaml.cs` — the shared viewport is the narrower pane's, and
  `PaneUnder` is now a question about the rendering rather than about the pointer

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — the tests that stated the old
  independence now state the synchronisation: `Viewer_MeasuresEachPaneByItsOwnLongestLine` →
  `Viewer_MeasuresTheSideBySideRenderingByItsWiderSide`, `Viewer_ScrollsOnePaneWithoutMovingTheOther`
  → `Viewer_ScrollsBothSidesTogether` (which asserts every row's cells share the one state and read
  the same offset), plus the new `Viewer_HasOneVerticalScrollForBothSides`; the wrap, reset and
  overlap tests drive the shared state

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-295F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One state or two mirrored | One | Two would desync where the sides' maxima differ, which is the normal case; one has nothing to keep in step |
| Two bars or one | Two, sharing the state | The request names the left and right scrollbars; each stays under its pane, and binding the same offset makes disagreement impossible rather than merely unlikely |
| What the wheel does | Moves the rendering, not a pane | With one scroll there is no "pane under the pointer" left to choose between |
| How the vertical claim is proved | A test on the rows' shape | The synchronisation is structural — one list, paired cells — so the thing worth pinning is that structure, not a scroll offset |

## Deviations & follow-ups

- **None from the plan.** All seven acceptance criteria are covered.
- **A deliberate contract change.** The old tests asserted the panes were independent ("the left is
  scrollable and the right is not", "moving the left leaves the right at 0"). Those statements are
  now false by design, and the tests say the opposite rather than being deleted.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1678  failed: 0  succeeded: 1678  skipped: 0
```

One test is new and five changed. No fix cycle was needed: the suite was green on its first run.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Neither describes how the diff scrolls sideways — they
describe the colouring, the two shapes and the context — so nothing in either was made untrue. The
capability is worth a line once the whole file is shown as well, which is PHASE02. There is no
`CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
