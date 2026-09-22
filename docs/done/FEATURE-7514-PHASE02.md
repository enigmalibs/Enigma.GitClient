# FEATURE-7514-PHASE02 — A manual merge section

**Item:** FEATURE-7514 — Branches, tags and remotes as dialogs
**Branch:** `feature/feature-7514-phase02-manual-merge`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

The Branches dialog has a **Merge** section above its list: a *Source* combo box, an *into* combo box,
and the **Merge**, **Merge fast-forward** and **Clear** buttons. It is a third way to say "merge this
into that", beside dropping one branch row onto another and the history's merge source.

- The source can be any branch, local or remote. The destination can only be local, the same policy
  the drop applies, because a remote branch is changed by pushing.
- **Merge** is `FastForwardMode.WhenPossible` and **Merge fast-forward** is `FastForwardMode.Only`. Both
  go through `IBranchDropOperations`, so the merge is exactly what dropping the source on the
  destination does: the destination is checked out first when it is not the current branch.
- The two merge buttons are enabled only for a pair the drop policy accepts (both chosen, not a branch
  into itself), and only while the page is not busy. **Clear** is enabled while either end is chosen.
- The lists are built from the whole repository, not the filtered list. They are rebuilt on every
  refresh, and each choice is kept by name while its branch exists.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `MergeSources`, `MergeDestinations`,
  `SelectedMergeSource`, `SelectedMergeDestination`, `ManualMergeRequest`, `ManualMergeCommand`,
  `ManualFastForwardCommand`, `ClearManualMergeCommand`, and `RebuildMergeChoices` run by every rebuild
- `Views/Pages/BranchesPageView.axaml` — the Merge section docked above the list

**Tests**

- `ManualMergeTests.cs` (new) — what the two lists hold; when each button is enabled; both merges ask
  the drop operations for the right request and mode (a remote source into a non-current branch); a
  merge into the current branch says so; Clear; the choices survive a filter and a refresh, and a
  choice goes with its branch; the view binds the section

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the combo boxes hold | Branch names (strings) | That is what the reader chooses by. What a merge needs to know about each one (remote? current?) is looked up from the last rebuild |
| A default destination | None | The prompt's Clear empties both, so an empty section is the state Clear returns to. A pre-filled destination would be a guess |
| Busy state | The merge buttons are disabled while the page runs an operation | Two merges started at once would race for the same checkout |

## Deviations & follow-ups

- One fix cycle, on the new view test: an inline `Avalonia.Controls` reference resolved against
  `Enigma.Avalonia` (the known namespace collision). The test now reads the generated fields.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1870 passed, 0 failed (8 new).
