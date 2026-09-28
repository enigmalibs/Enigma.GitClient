# FEATURE-292D — A resizable graph column

**Item:** FEATURE-292D — A resizable graph column
**Branch:** `feature/feature-292d-resizable-graph`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The history's graph is now a column like the others: it has a header titled **Graph** and a grip on
its right edge.

- Until the reader drags that grip, the column still follows the lanes in view, as before. From then on
  the width is theirs, and loading more lanes does not take it back. This is the pattern the Refs column
  already used.
- A graph made narrower than its lanes is clipped at the column's edge instead of drawing over the
  badges.
- The narrowest it can be is 24 px: one lane and its padding.
- It grows only into the room the message column can spare, like every grip.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryColumnLayout.cs`:
  - `HistoryColumn.Graph`;
  - `SeedGraphWidth` and `IsGraphWidthOwnedByReader`;
  - `Resize` treats Graph as a left-hand column;
  - minimum 24;
  - `WidthOf` and `SetWidth`.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the measured
  `GraphColumnWidth` seeds the layout instead of setting it.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml`:
  - the header's graph cell: title and `GraphGrip`;
  - the rows' `CommitGraphCell` takes `Columns.GraphWidth` and clips.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml.cs` — the grip resizes the graph.
- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — 3 tests:
  - seed / resize / owned;
  - the minimum and the slack;
  - headless: title, grip, rows aligned and clipped after a resize.
- `docs/roadmap.md`, `docs/plan/FEATURE-292D.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the enum member goes | First, `Graph` | It is the first column. The enum is never persisted, so the order is free. |
| `GraphWidth` setter | Stays public | The existing layout tests set it directly. The measured path goes through `SeedGraphWidth`. |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "a column header you can resize" is still accurate, and now covers the graph too. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2368 passed**, 0 failed (3 new). One fix cycle-free
  compile error (a `global::` qualifier in the new test) was fixed before the first test run.
