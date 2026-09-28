# FEATURE-5860-PHASE03 — Select the line in the history

**Item:** FEATURE-5860 — Context menus that do more
**Branch:** `feature/feature-5860-phase03-select-in-history`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

A branch line's menu (branches dialog) and a tag line's menu (tags dialog) now start with **Select in the
history**, followed by a separator. It does three things:

1. Closes the dialog.
2. Selects the commit's line in the history under it. For a tag this is the tagged commit, an annotated
   tag's included.
3. Brings the line into view: the list scrolls to its selected item.

- **A line that is not loaded yet** is found by reading further pages until it appears or the history
  ends.
- **A commit the graph does not draw at all**, because only a hidden branch reaches it, is reported in an
  info bar ("Not in the history … only branches hidden from the history reach it") instead of being
  selected.

How it works:

- `IToolDialogService.RevealInHistory(sha)` records the commit and hides the host.
- `ShowAsync` now returns `Task<string?>`, the commit to reveal, once the dialog has closed.
- `HistoryPageViewModel.OpenToolAsync` hands it to the new public `RevealAsync`, after any reload the
  dialog's operations called for.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/HistoryRevealTests.cs` — 5 tests:
  - through the real dialog hosts: branches, and tags with an annotated tag;
  - a commit past the loaded pages;
  - a hidden branch's commit, reported;
  - the selected line realised in view.

**Modified**

- `src/Enigma.GitClient.App/Services/ToolDialogService.cs` — `RevealInHistory`, and `ShowAsync` returning
  the commit.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `RevealAsync`, and
  `OpenToolAsync` using it.
- `src/Enigma.GitClient.App/ViewModels/Pages/BranchesPageViewModel.cs`, `TagsPageViewModel.cs` — take
  `IToolDialogService`; `SelectInHistoryCommand` on the page and on each row.
- `src/Enigma.GitClient.App/Views/Pages/BranchesPageView.axaml`, `TagsPageView.axaml` — the menu item.
- `docs/roadmap.md`, `docs/plan/FEATURE-5860.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Scrolling to the line | The list's own `AutoScrollToSelectedItem` | It already brings a selection made from the model into view. The test proves the container is realised. |
| A read in flight when revealing | Waited for, polling `IsBusy` every 10 ms | Its rows are the ones to search. `LoadPageAsync` always clears `IsBusy`, so the wait ends. |
| Where the item sits | First in the menu | It is about the line itself, not an operation on the repository |
| A diff view open over the history | Closed | The selected line has to be visible |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

No README section lists these menus. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2441 passed**, 0 failed (5 new), with no fix cycle.
