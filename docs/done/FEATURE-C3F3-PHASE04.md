# FEATURE-C3F3-PHASE04 — Remove and open from branch menus

**Item:** FEATURE-C3F3 — Remove worktrees, mark their branches
**Phase:** PHASE04 — Remove and open from branch menus
**Branch:** `feature/feature-c3f3-phase04-worktree-remove-flow`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

A worktree can now be removed from the application: the branches dialog's row menu and the history's
badge menu offer it, together with three ways to open the worktree. Removal says what it costs first,
and can delete the worktree's branch in the same step.

- **`IWorktreeOperations.RemoveAsync(worktree)`**:
  - **Refuses** the main working tree, the open one and a locked one, before reading or asking anything.
    It uses Core's own `WorktreeService.FindRemovable` sentences, as a warning *Could not remove the
    worktree*.
  - **Probes** with `CountChangesAsync`. For a worktree on a branch it also reads whether the branch is
    merged and, if not, its unmerged commits: at most `MaximumListedCommits` subjects, plus the full
    count, as the delete flow reads them. A probe git cannot answer is reported as an error and asks
    nothing.
  - **Asks** with its own confirmation (`RemoveWorktreeDialogViewModel` + `RemoveWorktreeDialogView`),
    in the destructive look, *Cancel* the default:
    - title *Remove worktree*, and "Remove the worktree at <path>? Its folder is deleted.";
    - for a missing folder, "Its folder is already gone; this removes git's record of it.";
    - with changes, in the warning colour, "N uncommitted change(s) in it will be lost.";
    - always, "Close any window working in it first.";
    - the checkbox *Also delete the branch "x"*, **ticked when the branch is merged and unticked when it
      is not**, with "holds N commits the current branch does not; deleting it loses them" and the
      subjects under it ("…and N more" past the limit). No checkbox for a detached worktree;
    - the primary button says *Remove*, or *Remove anyway* when there are changes.
  - **Writes**, under `RunExclusiveAsync`: `IWorktreeService.RemoveAsync(force: changes > 0)`, then,
    when the box is ticked, the branch delete through `IBranchService` (`-d` merged, `-D` unmerged).
  - **Reports** *Worktree removed*: "'<path>' is gone." or "'<path>' is gone and \"x\" is deleted.". A
    branch delete that fails after the removal is a warning that still reports the removal and names
    the branch with git's reason. It returns whether the worktree was removed.
- `OpenAsync` uses `IInstanceLauncher`, `OpenFolderAsync` uses `ISystemInterop.OpenPathAsync`, and
  `OpenTerminalAsync` uses `ISystemInterop.OpenTerminalAsync`. Each says so when it fails, in the
  toolbar's words: *No new window*, *The folder did not open*, *No terminal started*.
- **Menus.** For a local branch another working tree has checked out, the branches dialog's row menu
  and the history's badge menu offer the same four items, in the same order, right after *Create
  worktree…*:
  - *Open worktree in a new window*, *Open worktree folder*, *Open a terminal in the worktree*: greyed
    for a missing folder;
  - *Remove worktree…*: absent for the main working tree.

  `BranchRowViewModel` gains `CanRemoveWorktree` and the four commands, which come from the page.
  `HistoryBranchViewModel` gains `Worktree` (read live through the new `HistoryWorktreeCommands`),
  `IsInOtherWorktree` and `CanRemoveWorktree`.
- **After a removal,** the branches page rebuilds, and the history reloads as it does after a branch
  deletion.

## Files touched

- Created:
  - `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/RemoveWorktreeDialogViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Dialogs/RemoveWorktreeDialogView.axaml` (+ `.axaml.cs`)
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreeRemoveTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/WorktreeOperations.cs`: `RemoveAsync`, the three opens, and
    `IBranchService` and `ISystemInterop` added to the constructor.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`: the four commands.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: `HistoryWorktreeCommands`
    and the badge's worktree properties.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: the commands, and the
    reload after a removal.
  - `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`,
    `Views/Pages/HistoryPageView.axaml`: the four menu items.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| The refusals before the question | `WorktreeService.FindRemovable` over the context's list | "Refuses, as Core does", in Core's own sentences, before anything is read | Copying the sentences |
| A probe git cannot answer | An error with git's first line; nothing asked | The question would claim the worktree is clean when nothing was read | Treating it as 0 changes |
| How the history badge reaches the worktree | A `WorktreeOf` delegate in `HistoryWorktreeCommands`, read when asked | The badge already carries only the tip text; a live lookup keeps it right after a refresh | Copying the `GitWorktree` into `RefBadgeItem` |
| The four menu items' look | `AppWindow`, `FolderOpen`, `TerminalWindow`, `FolderMinus` icons | Every menu item has a glyph (`MenuIconTests`). `FolderMinus` pairs with *Create worktree…*'s `FolderPlus` | `Trash` (that is for deleting a branch) |
| A failed branch delete after removal | One warning: the removal and the branch with git's reason | The plan: "a warning naming the branch; the removal is still reported" | Two notifications |
| The confirmation's look | The danger class, *Cancel* default | The house look for a destructive question (`ConfirmDestructiveAsync`) | A plain dialog |
| A locked worktree in the menus | *Remove worktree…* stays enabled; the operation refuses it, naming `git worktree unlock` | The plan greys it only in PHASE05's dialog, where the row says why | Greying it without a reason |

## Deviations & follow-ups

- **No new registration.** Step 4 asked to register what is new and extend `CompositionRootTests`. The
  only new service, `IWorktreeGuard`, came in PHASE03 and is covered there. The dialog view model is
  built by the flow, as the create dialog's is. `WorktreeOperations` now also takes `IBranchService`
  and `ISystemInterop`, and `CompositionRootTests` already resolves `IWorktreeOperations`.
- **Manual check pending:** the dialog's layout, and the warning colour on a dark and a light theme.

## Documentation sweep

- Nothing changed in this phase. PHASE05 updates the README's feature list for removal, the dialog and
  the marks together.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1255 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction: run on Linux
  before tagging). `WorktreeRemoveTests` adds 20 tests:
  - **the dialog:**
    - a merged branch ticked;
    - an unmerged one unticked, with its commits and "…and N more";
    - the changes warning and *Remove anyway*;
    - the "close any window" line;
    - no checkbox when detached;
    - the missing folder's question;
  - **removal, against real worktrees:**
    - ticked: folder and branch gone;
    - unticked: the branch kept, then deletable;
    - an unmerged branch offered unticked and kept;
    - with changes: named, and removed with force;
    - cancelled: nothing removed;
    - a branch delete failing after the removal (a ref lock): a warning naming the branch;
    - the main working tree and a locked worktree refused with no question;
  - **opening:** launch and its failure; folder and terminal and their failures;
  - **menus:**
    - the branches dialog's items only for such a branch, and the page rebuilding after a removal;
    - the realised row menu's four items in order, right after *Create worktree…*;
    - no *Remove* for the main working tree seen from a linked one;
    - a missing folder greying the three *Open* items while *Remove* still clears the record;
    - the history badge's items only for such a branch, and the history reloading after a removal.
