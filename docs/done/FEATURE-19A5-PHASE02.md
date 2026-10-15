# FEATURE-19A5-PHASE02 — The worktree dialog

**Item:** FEATURE-19A5 — Create a worktree from a branch or line
**Phase:** PHASE02 — The worktree dialog
**Branch:** `feature/feature-19a5-phase02-worktree-dialog`
**Run:** feature/2026-10-15-worktrees-toolbar

## Summary

The *Create a worktree* dialog and the flow behind it, shared by every place that will offer it
(PHASE03 adds the menu items).

- **The dialog** (`CreateWorktreeDialogViewModel`, `CreateWorktreeDialogView`), from top to bottom:
  - **Branch to create**, judged by `RefNameValidator` and refused when a local branch has that name.
  - **Starts from**: the commit or branch the menu was opened on, read-only.
  - **Working directory**:
    - proposed as `<parent>/<repository>.worktree.<branch>`, next to the repository
      (`WorktreePaths.DefaultFor`); it follows the branch as it is typed (`feature/login` →
      `project.worktree.feature-login`) until the reader edits it, and a folder the reader typed is
      never overwritten;
    - *Browse* picks the folder the worktree goes in. While the field follows the branch, the
      proposed name is put there and keeps following; otherwise the typed folder name is kept;
    - judged by `WorktreePaths.Validate`: not a full path, inside the repository, a file, a non-empty
      directory.
  - **Open it in a new window once it is created**, checked by default.
  - The validation line says why *Create* is greyed: the branch first, then the folder.
- **The flow** (`IWorktreeOperations.CreateAsync(startPoint, startPointLabel)`):
  - the form (*Create a worktree*, *Create*/*Cancel*, *Create* enabled only while the fields are
    usable);
  - `IWorktreeService.AddAsync` under `RunExclusiveAsync`, so the repository is read again after and
    the new branch appears;
  - then:
    - *Worktree created — "feature/login" is checked out in '…'.*;
    - and, when asked, `IInstanceLauncher.Launch(folder)` opens it beside this window. A window that
      cannot start says *No new window — the worktree is in '…', but another instance … could not be
      started*.
  - A refusal is a warning and a git failure an error with git's first line, as for branches.
  - Returns whether a worktree was created, so a page can reload.
- Registered as a singleton; `CompositionRootTests` resolves it.

## Files touched

- Created:
  - `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/CreateWorktreeDialogViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Dialogs/CreateWorktreeDialogView.axaml` (+ `.axaml.cs`)
  - `src/Enigma.GitClient.Desktop/Services/WorktreeOperations.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreeTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`:
    `IWorktreeOperations`.
  - `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`: one `InlineData`.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| The form plumbing | Inline in `WorktreeOperations`, as `TagOperations` does | `BranchOperations.ShowFormAsync` is private; extracting a shared helper is a refactor outside this item | Making it a shared extension now |
| The path property's name | `WorkingDirectory` | The draft's own word; `Path` would hide `System.IO.Path` inside the class | `Path`, `Folder` |
| What *Browse* picks | The folder the worktree goes in (its parent), keeping the worktree's folder name | A folder picker only picks existing folders, and the worktree's folder should not exist yet; the create-repository dialog's *Browse* works the same way | Picking the worktree's folder itself |
| When the validation line speaks | At once, as the create-branch dialog does | The closest dialog in the app; *Create* is greyed and the line says why | Silence until the first keystroke (the profile dialog's way) |
| A window that cannot start | Error naming where the worktree is; the operation still reports success (`true`) | The worktree exists: the page must reload, and the reader must know where to find it | Treating it as a failure |
| The success message when a window opens | Shown too | The new window can take a moment; the message says it worked | Silence when a window opens |

## Deviations & follow-ups

- No deviation from the plan.
- **Follow-up:** `BranchOperations`, `TagOperations`, `ProfilesPageViewModel` and now
  `WorktreeOperations` each wire a form's validation to the primary button by hand. A shared
  `ShowFormAsync` extension on `IContentDialogService` would remove four copies. Not done here.

## Documentation sweep

- Nothing changed: nothing reaches the dialog until PHASE03 adds the menu items, where the README
  gets its line.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1220 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests`: **372 passed, 2 skipped, 0 failed** (3 min 21 s).
- Desktop tests added in `WorktreeTests` (12) and `CompositionRootTests` (1 row), **compiled, not
  run**, under the project's standing instruction never to run the Desktop suite on this Windows
  machine. Run them on Linux before tagging.
  - The proposal: beside the repository, follows the branch, stops once edited.
  - Each validation message, and `ValidationChanged` as it is typed.
  - *Browse*: in the chosen folder, still following; a typed name kept; a cancelled picker changes
    nothing.
  - Confirmed: the folder exists on the new branch, a window is started on it, the branch is in the
    context's refs, HEAD did not move, *Create* was greyed until a name was typed.
  - Unchecked: nothing started. Declined: nothing created. Window failing: the error names the
    folder. No repository: nothing asked.
