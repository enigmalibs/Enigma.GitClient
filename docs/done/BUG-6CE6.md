# BUG-6CE6 — Details panel ignores the splitter

**Item:** BUG-6CE6 — Details panel ignores the splitter
**Branch:** `bugfix/bug-6ce6-details-panel-resize`
**Run:** bugfix/2026-09-16-history-panel-and-graph

## Summary

The history page's bottom panel — the selected commit, its files and its diff — now follows the
splitter above it.

It never did before, and the reason was one word in the XAML. The workspace grid was
`RowDefinitions="*,Auto,Auto"` and the panel itself carried `Height="300"`. A `GridSplitter` writes
a drag back into the row definitions it sits between **keeping each definition's unit type**: the
star row was handed a star, and the detail row — `Auto` — was handed a value that an `Auto` row has
no use for, because an `Auto` row measures to its content. Its content was a `Border` pinned at 300.
So the drag was recorded, the layout was recalculated, and everything came out exactly 300 again.

The detail row is now an absolute length and the panel has no height of its own, which is the shape
`ConflictResolutionPageView` already uses for the same arrangement. The graph row gained a
`MinHeight` so the splitter can shrink the list but never erase it.

That left one thing the old `Auto` row did for free: taking no space when nothing is selected. A
pixel row would have reserved 300 px of empty surface the moment the page opened. The view's
code-behind therefore follows the page's `HasSelection` and opens the row to its height or collapses
it to zero — reading back whatever the splitter last left behind before collapsing, so a reader who
drags the panel to 420 and clicks away gets 420 back, not the 300 default.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml` — explicit row definitions (star + `MinHeight` 120, the
  splitter's `Auto` row, an absolute detail row starting at 0), the named `Workspace` grid, and the
  named `CommitDetails` border with its fixed height removed
- `Views/Pages/HistoryPageView.axaml.cs` — `DefaultDetailsHeight` / `MinimumDetailsHeight`, the
  subscription to the page's `HasSelection`, and the row-height translation

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — four cases under "the detail panel"

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/BUG-6CE6.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the height lives | On the row definition, not on the panel | It is what the splitter writes to; anything else is a value the splitter cannot reach |
| Collapsing with no selection | Code-behind, from the ViewModel's `HasSelection` | A `RowDefinition` is not a control and does not inherit a `DataContext`, so a binding on its height is the fragile way to say this; a view translating a ViewModel fact into layout is the ordinary way |
| The remembered height | A field on the view, read back before collapsing | Re-opening at the default would throw away the size the reader just chose, which is the same complaint in a different form |
| Minimums | 120 px for the graph, 140 px for the details | A splitter that can erase either side leaves the reader with no way back |
| How the drag is tested | By writing the row's length, not by simulating a pointer | The dragged length on the row *is* the splitter's whole output; the test asserts the panel follows it, and it fails on the old XAML, which is what a regression test is for |

## Deviations & follow-ups

- **Follow-up:** the panel's height is remembered for as long as the window lives, not across runs.
  Persisting it would mean a new `AppSettings` property, which this report did not ask for.
- **Follow-up:** the same arrangement on `ChangesPageView` (two star rows) and
  `ConflictResolutionPageView` (an absolute row) resizes correctly and was left alone.
- **Observation:** one full-solution run reported a single error in the integration project with no
  failing test named; two further runs of the whole suite and a direct run of that project (284
  tests) were green. Nothing in this dev touches `Enigma.GitClient.Core`.
- **Line endings (recommendation only):** no CRLF churn observed in the touched files. No action
  taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1568  failed: 0  succeeded: 1568  skipped: 0
```

Four tests are new: the panel takes no room until a commit is selected and opens at 300 when one is;
its height comes from its row rather than from a fixed height (the regression — it fails on the old
XAML); the height a drag leaves survives a deselect and a re-selection; and the graph keeps its
floor whatever the splitter is dragged to.
