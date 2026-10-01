# FEATURE-A35A-PHASE02 — The working tree's files too

**Item:** FEATURE-A35A — A selected file lets go on a click
**Phase:** PHASE02 — The working tree's files too
**Branch:** `feature/feature-a35a-phase02-working-tree-toggle`
**Run:** feature/2026-10-01-polish-release-5-1

## Summary

On the history's uncommitted line, a click on the selected file of *Not staged* or *Staged* now lets
go of it and puts its diff away, as for a commit's files. The uncommitted line and its panel stay.

- **`WorkingTreePanelViewModel.SelectionReleased`:** raised when either half's `SelectionReleased`
  fires, which PHASE01 made the panel's click-only let-go. A refresh, or one half giving the selection
  up to the other, never raises it.
- **`HistoryPageViewModel`:** `WorkingTree.SelectionReleased` sets `IsDiffViewOpen = false`, the same
  as `Files.SelectionReleased`.
- The gesture itself needed nothing new. Both halves are `ChangedFilesPanelView`s, and PHASE01 taught
  that view the toggle. So the row's *Stage* / *Unstage* button is left alone on the selected line, as
  its tests below show.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Panels/WorkingTreePanelViewModel.cs`: `SelectionReleased`,
  forwarded from both halves.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs`: it closes the diff.
- `tests/Enigma.GitClient.App.UnitTests/FileListToggleTests.cs`: 4 new tests over a working tree with
  `README.md` not staged and `docs/staged.md` staged, shown on the uncommitted line, plus the
  `ShowWorkingTreeAsync` and `RowButtonOf` helpers:
  - a theory, once per half: the selected file lets go on a click, the change and the diff go, the
    uncommitted line keeps its panel, and the next click brings them back;
  - the selected file's *Stage* button stages it without letting go. The file stays picked on its way
    to *Staged*, its diff still open;
  - a file in the other half takes the selection and the diff stays open on it.
- `docs/roadmap.md`, `docs/plan/FEATURE-A35A.md`: statuses. The item is `DONE` with its last phase.

**Created**

- `docs/done/FEATURE-A35A-PHASE02.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Forward the halves' release, or let the history listen to both halves | Forwarded by the working tree | The history already talks to the working tree as one thing (`SelectionChanged`, `SelectedChange`). Its halves are its own business |
| Proving the tests | Ran the class with the history's new hook removed | Both theory cases failed (the diff stayed open); the other tests passed both ways |

## Deviations & follow-ups

- **None from the plan.**
- The test helper's first compile hit the `Enigma.Avalonia` ↔ `Avalonia` namespace collision (an
  inline `Avalonia.Automation…`). It became a file-scope `using`.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing in `README.md` or the other docs describes how a working-tree file is picked. The change goes
into the 5.1.0 notes with FEATURE-0C53. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2639 passed**, 0 failed, 0 skipped (4 new).
- Fix budget: 0 cycles used.
