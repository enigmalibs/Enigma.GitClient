# FEATURE-8CC5 — The first file, every time

**Item:** FEATURE-8CC5 — The first file, every time
**Branch:** `feature/feature-8cc5-first-file-selected`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

The diff view now always opens on the commit's **first file**, and its diff is loaded. The file that
was selected before is never selected again: not in another commit (even one that touches the same
path), and not in the same commit reopened.

- `ChangedFilesPanelViewModel.SelectFirstFile()` selects the first file in display order: the list's
  first row, or the tree's first file depth first. It opens any collapsed directories above that file,
  so its row can be seen.
- `SetFiles(files, keepSelection)`: the history passes `false`, so a new commit's list starts with
  nothing selected. The changes page and the stash panel keep the default (`true`), and with it their
  selection across a refresh.
- `HistoryPageViewModel` remembers which row its files belong to (`_filesRow`). It selects the first
  file when the diff view opens, and again when the files arrive while the view is open. It never
  selects among files that are still the previous row's.
- A row that is only selected (not opened) has no file selected, so no patch is read for a view nobody
  is looking at.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Panels/ChangedFilesPanelViewModel.cs` —
  `SetFiles(…, keepSelection)`, `SelectFirstFile()`, `Rebuild(string? previous)`, `SelectedFilePath`
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `_filesRow`, the first file
  selected by `IsDiffViewOpen` and by `LoadChangedFilesAsync`, and the remarks on `IsDiffViewOpen`
- `tests/Enigma.GitClient.App.UnitTests/ChangedFilesPanelTests.cs` — eight new tests:
  - on the panel: the list's first row; the tree's first file with its collapsed directory opened; an
    empty panel; `keepSelection: false`;
  - on the history page: the diffs open on the first file with its diff loaded; the selected file is
    never carried to another commit that has it; the same commit reopens on its first file; a row that
    is only selected has no file selected.
- `docs/roadmap.md`, `docs/plan/FEATURE-8CC5.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to know the panel's files are the selected row's | Remember the row the files were set for, and compare references | `_diffTarget` is set before the files arrive, so it cannot tell stale files from current ones |
| Where `SelectFirstFile` runs on open | In the `IsDiffViewOpen` setter | Every way into the view (double-click, the row's menu, a test setting it) gets the rule |
| The test for "files arrive after the view opened" | *Show changes* straight after a reload, then wait for the selection | That is the order the page actually runs in: the view opens first, the files come after |

## Deviations & follow-ups

- None from the plan.
- Documentation sweep: no README or prose doc describes which file the diff view selects, so nothing
  was edited.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2239 passed**, 0 failed (2231 before, plus 8 new).
