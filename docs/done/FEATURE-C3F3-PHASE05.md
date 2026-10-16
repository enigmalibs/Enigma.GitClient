# FEATURE-C3F3-PHASE05 — The Worktrees tool dialog

**Item:** FEATURE-C3F3 — Remove worktrees, mark their branches
**Phase:** PHASE05 — The Worktrees tool dialog
**Branch:** `feature/feature-c3f3-phase05-worktrees-dialog`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

There is now a fourth tool dialog, *Worktrees*. It lists every working tree of the repository and
offers to open, show the folder of, open a terminal in, or remove each one. Where an action does not
apply, the menu says why.

- **Opening it:** `ToolDialog.Worktrees` maps `WorktreesPageView` + `WorktreesPageViewModel` in
  `ToolDialogService`. The history toolbar has a new button `OpenWorktrees` (the `Tree` icon, tip
  *Worktrees*) right after *Branches*, bound to `OpenWorktreesCommand` and enabled while a repository
  is open. A removal made from the dialog reloads the history when it closes, because the worktrees
  are part of the state stamp (PHASE02).
- **Rows** (`WorktreeRowViewModel`) come from `IRepositoryContext.Worktrees`: the main one first,
  then the others by path. Each row shows:
  - the folder's name, with the full path as its tooltip;
  - the branch, "detached at <short sha>", or "bare";
  - badges *main*, *this window*, *locked* (the `Lock` icon, tooltip "Locked: <reason>") and *folder
    missing* (the `FolderDashed` icon), each with its tooltip and `AutomationProperties.Name`.

  The rows are rebuilt on every refresh and when the repository changes.
- **Row menu** (a double-click does *Open in a new window* too), each item going through
  `IWorktreeOperations`:
  - *Open in a new window*, *Open folder*, *Open a terminal*: greyed for a missing folder, with the
    tip "Its folder is missing".
  - *Remove…*: greyed, with a tip saying why, for:
    - the main working tree: "The main working tree cannot be removed";
    - this window: "Remove it from another window of this repository";
    - a locked worktree: "Locked: <reason> — unlock it with git worktree unlock".

  A worktree whose folder is missing can still be removed, which clears git's record of it. The greyed
  items show their tips through `ToolTip.ShowOnDisabled`.
- **Empty state** when only the main working tree exists: "No other worktrees. Create one from a
  branch's or a commit's menu." With no repository open: "Open a repository to see its worktrees."
- `WorktreesPageView` (transient) and `WorktreesPageViewModel` (singleton) are registered like the
  other pages.
- **Documentation sweep:** the README's feature list now covers the marks, the menus, removal, the
  dialog and the up-front refusals.

## Files touched

- Created:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/WorktreesPageViewModel.cs` (with
    `WorktreeRowViewModel`)
  - `src/Enigma.GitClient.Desktop/Views/Pages/WorktreesPageView.axaml` (+ `.axaml.cs`: the line menus
    and the double-click)
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreesDialogTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/ToolDialogService.cs`: `ToolDialog.Worktrees` and its
    mapping.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: `OpenWorktreesCommand`.
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the toolbar button.
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: the page.
  - `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`: the page view model resolves.
  - `tests/Enigma.GitClient.Desktop.UnitTests/ToolDialogTests.cs`: the dialog shows its page, and the
    toolbar button opens it.
  - `README.md`: the feature list.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| The empty state with only the main working tree | The house `EmptyState` alone; no list | The plan's sentence is the whole content, and the toolbar already opens the main working tree's folder and terminal | The main row with the message under it |
| The menu's words | *Open in a new window*, *Open folder*, *Open a terminal*, *Remove…* | Inside the Worktrees dialog every line is a worktree, so "worktree" in each item would only repeat it. The icons match the branch menus' | The branch menus' longer headers |
| The double-click | A `DoubleTapped` handler in the view that runs the row's `OpenCommand` when it can | The plan's "double-click for *Open in a new window*"; the command stays the one place that knows whether it applies | A behaviour package |
| The greyed items' tips | `ToolTip.ShowOnDisabled`, as the history toolbar and the diff viewer use | A disabled item shows no tooltip otherwise, and the plan asks the item to say why | A separate line of text |
| A missing folder's *Remove…* | Enabled | Removal is what clears git's record (PHASE01), and the plan only greys the *Open* items there | Greyed |
| Sorting the linked worktrees | By path, case-insensitive on Windows | "main first, then by path", compared as paths are everywhere else in the application | git's order |
| The README's badge list | "checked out in another worktree" added | The list names every badge a branch line can carry, and it is factually incomplete without the new one | Leaving it |

## Deviations & follow-ups

- No deviation from the plan.
- Not covered by `ALongList_ScrollsUnderItsHeader_WhichStaysAtTheTop`: that theory needs dozens of rows,
  which here would mean dozens of real worktrees. The dialog uses the same `rows` list style as the
  tags dialog, which that test covers.
- **Manual check pending:** the dialog in both themes, and the double-click on a real desktop.

## Documentation sweep

- `README.md` feature list:
  - the worktree item now covers the tree marks, the four menu items, removal (with or without the
    branch), the *Worktrees* dialog, the up-front refusals and the strip's mark;
  - the branches' badge list adds "checked out in another worktree".
- Not touched: the "What's new in 6.3" callout. The release (FEATURE-C56E) rewrites it for 6.4.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**, AVLN included.
- `Enigma.GitClient.Core.UnitTests`: **1255 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction: run on Linux
  before tagging).
  - `WorktreesDialogTests` adds 10 tests:
    - the rows' order, names and checkouts;
    - the four badges, and *this window* from a linked worktree;
    - *Remove…* greyed with each reason, and allowed for a missing folder;
    - the *Open* items greyed only for a missing folder;
    - the three *Open* items reaching the launcher and the desktop;
    - the empty states;
    - the realised badges with their tips, and a double-click launching the worktree;
    - a real removal from the dialog, after which the rows follow.
  - `ToolDialogTests`: the Worktrees dialog shows its page, and the toolbar button opens it.
  - `CompositionRootTests`: `WorktreesPageViewModel` resolves.
