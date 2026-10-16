# FEATURE-C3F3-PHASE02 — Worktrees in the state, tree marks

**Item:** FEATURE-C3F3 — Remove worktrees, mark their branches
**Phase:** PHASE02 — Worktrees in the state, tree marks
**Branch:** `feature/feature-c3f3-phase02-worktree-marks`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

The application now knows the open repository's working trees. A branch checked out in another working
tree is marked with Phosphor's `Tree` in two places: the branches dialog and the history. The
repository strip says when the open repository is itself a worktree.

- **`IRepositoryContext`** gains four members:
  - `Worktrees`: every working tree, the main one first. It is empty while closed.
  - `IsLinkedWorktree`: the open repository is not its main working tree.
  - `MainWorktreePath`.
  - `WorktreeOf(branch)`: the working tree that has the branch checked out, other than the open one.
    That includes the main working tree, seen from a linked one.

  `RepositoryContext.RefreshAsync` lists the worktrees (`IWorktreeService.ListAsync`) while it reads
  the references, and publishes both together. A list that fails is logged as a warning and left
  empty; the references are still published. Opening another repository or closing clears the list.
- **`RepositoryStateStamp`** now also covers each working tree's folder, branch, main flag and missing
  flag, so the history redraws its pills when a worktree comes or goes.
- **Branches dialog:** `BranchRowViewModel` gains:
  - `Worktree`, `WorktreePath`, `IsInOtherWorktree`, local rows only;
  - `WorktreeTip`: "Checked out in the worktree at <path>", or "Checked out in the main working tree
    at <path>", with " (its folder is missing)" when it is.

  The page passes `IRepositoryContext.WorktreeOf` for every local row. The badge comes right after
  *checked out*: the `Tree` icon (`pill faint`) and the word "worktree", with the tip as its tooltip
  and `AutomationProperties.Name`.
- **History pill:** `RefBadgeItem` gains `Worktree` (the tip) and `IsWorktree`. The history page
  fills them from the context when it builds its rows, through the new `CommitRowViewModel` parameter
  `worktreeOf`.
  - `RefBadge` gains `IsWorktree` and `WorktreeTip`. The template draws a 13 px `Tree`
    (`WorktreeIcon`) after the check and before the kind icon.
  - The pill's tooltip and accessible name add the tip on a second line.
  - `RefBadgeMetrics.MeasureBadge(…, isWorktree)` counts the extra icon, and `Measure` passes it, so
    the Refs column is still seeded to fit.
- **The current branch never gets the tree,** on either surface. It keeps its *checked out* badge and
  its check.
- **Repository strip:** for a linked worktree, a `Tree` after the folder name, with the tooltip
  "A worktree of <main path>" (`MainWindowViewModel.IsLinkedWorktree` / `LinkedWorktreeTip`).
- `Formatting/WorktreeText` holds the shared wording: `Location` and `CheckedOutTip`. PHASE03's
  refusals will use the same words.

## Files touched

- Created:
  - `src/Enigma.GitClient.Desktop/Formatting/WorktreeText.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeWorktreeService.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreeMarkTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/IRepositoryContext.cs`, `RepositoryContext.cs`,
    `RepositoryStateStamp.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`,
    `CommitRowViewModel.cs`, `HistoryPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Controls/RefBadge.cs`, `RefBadgeMetrics.cs`
  - `src/Enigma.GitClient.Desktop/Themes/Controls.axaml`
  - `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`, `Views/Pages/BranchesPageView.axaml`,
    `Views/Pages/HistoryPageView.axaml`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/TestServices.cs`: a fake list beside the
    fake reader.
  - `BulkDeletionTests.cs`, `HiddenBranchesTests.cs`, `RepositoryContextTests.cs`,
    `ToolDialogTests.cs`: the context's new constructor argument.
  - `HistoryGroupedBadgeTests.cs`: the icon order is read over the visible icons, because the tree is
    always in the template.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| How the context reads the worktrees | `IWorktreeService.ListAsync`, started together with the reference read | One more git process per refresh, run alongside the references so it adds no wait. A failed list must not fail the refresh | Reading after the references; folding it into Core's `IRefReader` |
| The fake reader's tests | `TestServices` swaps in `FakeWorktreeService` with `FakeRefReader`: its list answers synchronously, and its writes go to the real service | Those tests rely on every refresh completing synchronously so they can block the UI thread. A real git list would hang them | Leaving the real service, which would deadlock them |
| Where the "not the current branch" rule lives | In the row and in the pill's projection, not in `WorktreeOf` | `WorktreeOf` stays a plain fact for PHASE03's guards. The display rule is the plan's: "the open window's own branch keeps its badge, never the tree" | Filtering in `WorktreeOf` |
| The pill's tooltip | The tip on a second line under the name (`Description`) | The acceptance criterion asks the tooltip to name the folder on both surfaces | A bool alone, with no folder on the pill |
| `RefBadgeItem`'s new field | `string? Worktree` (the tip) with `IsWorktree` computed | One field carries both the mark and the words. `IsWorktree` reads as the plan names it | A bool plus a separate tip field |
| The history redrawing on a worktree change | The worktrees are part of `RepositoryStateStamp` | The history keeps the reader's place unless the stamp moves, and a new worktree changes what the pills draw | A separate event |
| Hit target of the strip's tree | A transparent `Border` around the icon carries the tooltip | An icon is hit only where it has ink (house gotcha) | The tooltip on the icon itself |
| The shared wording | `Formatting/WorktreeText` | The row, the pill and PHASE03's refusals must say "the worktree at …" the same way | Three copies |

## Deviations & follow-ups

- **The pill's tooltip names the folder.** The plan's step 3 only drew the tree; `RefBadge` also gains
  `WorktreeTip` because the acceptance criterion asks for the folder on both surfaces.
- **The history redraws when the worktrees change**, through `RepositoryStateStamp`. The plan did not
  name it; without it a new or removed worktree would not show until the next commit or ref change.
- **Manual check pending:** the headless suite cannot judge how the tree looks next to the check. The
  Linux Desktop run and a look in the application before tagging cover it.

## Documentation sweep

- Nothing changed. The README's worktree line is about creating one and is still right. PHASE05
  updates the feature list for removal, the dialog and the marks.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**, AVLN included.
- `Enigma.GitClient.Core.UnitTests`: **1255 passed, 1 skipped, 0 failed** (Core is unchanged in this
  phase).
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction: run on Linux
  before tagging). `WorktreeMarkTests` adds 12 tests:
  - the row badge on a branch checked out in another worktree, and on no other row, the current
    branch included;
  - the main working tree's branch marked when seen from a linked worktree;
  - the "folder is missing" tip;
  - the realised badge: the tree, the word, the tooltip and the automation name;
  - the history pill's `IsWorktree` and tip, with the Refs column still seeded from the measurement;
  - the realised pill's tree, only on the worktree's branch;
  - the tree's place (after the check, before the branch icon), its size, and its width as drawn
    and as measured;
  - the strip's mark only for a linked worktree, with its tip;
  - the context:
    - `WorktreeOf` from the main and from a linked working tree;
    - a failing list still publishes the references;
    - closing clears the list;
    - the stamp moves when a worktree comes.
