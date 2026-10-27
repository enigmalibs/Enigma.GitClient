# FEATURE-075D — Rename a branch from its badge

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-075d-rename-from-badge`
**Run:** feature/2026-10-27-rename-branch-badge

## Objective

A local branch's badge on the History page offers *Rename "<name>"…* in its context menu. It opens
the content dialog the Branches page already uses to rename a branch: the name can be changed and is
validated as it is typed, and *Rename* renames the branch while *Cancel* changes nothing. Then the
history reloads and the badge carries the new name.

## Context & constraints

- **Already there, reused as is:**
  - Core: `IBranchService.RenameAsync` (`git branch -m`).
  - Desktop: `IBranchOperations.RenameAsync(string name)` (`Services/BranchOperations.cs`) shows the
    dialog *Rename branch* (`RenameBranchDialogView` + `RenameBranchDialogViewModel`), with
    *Rename* / *Cancel*. *Rename* stays disabled while the name is invalid (`RefNameValidator`), is
    the current one (*That is the name it already has.*) or is taken (*A branch called "x" already
    exists.*). The write runs under the repository lock, and a failure says *Could not rename "x"*
    with git's reason. The interface's remarks require both the Branches page and the graph to go
    through it.
  - The Branches page's row menu has *Rename…* (`PencilSimple`), enabled for local branches only.
- **The badge menu:**
  - It is declared in `Views/Pages/HistoryPageView.axaml`, in the `HistoryBranchViewModel`
    `DataTemplate` (~l.660–803), and is bound to `HistoryBranchViewModel` (`ViewModels/Pages/HistoryBranchViewModel.cs`).
  - Its commands are the `HistoryBranchCommands` record, built once in `HistoryPageViewModel`'s
    constructor (~l.227). Optional commands come last and default to `null` (`Copy`,
    `CreateWorktree`, `Worktrees`), with a `Can…` property that hides the item without them.
  - A local branch and its upstream on one commit are one badge (the local one's). The remote half
    is reached only through *Delete "origin/x"…*.
- **The dialog takes no focus today**: opened from a menu, the focus stays behind it. The tag and
  squash dialogs fix that in their code-behind (`Loaded` → `Dispatcher.UIThread.Post` → `Focus()`).
- **Merge source:** `ForgetAMergeSourceThatIsGone` already drops a merge source whose branch was
  renamed, on reload.
- **Tests on this machine (Windows):** the Desktop suite is compiled, not run (house rule). The Core
  suites run.

## Steps

1. `HistoryBranchViewModel.cs`:
   - `HistoryBranchCommands` gains an optional last parameter
     `AsyncRelayCommand<HistoryBranchViewModel>? Rename = null`, documented like its neighbours.
   - `HistoryBranchViewModel` gains `CanRename => IsLocal && Commands.Rename is not null` and
     `RenameHeader => $"Rename \"{Name}\"…"`.
2. `HistoryPageViewModel.cs`:
   - Pass `new AsyncRelayCommand<HistoryBranchViewModel>(OnRenameBranchAsync, branch => branch?.CanRename == true)`.
   - `OnRenameBranchAsync` sits beside `OnDeleteBranchAsync`. It calls
     `_branchOperations.RenameAsync(branch.Name)` and reloads when it returns `true`.
   - The `ForgetAMergeSourceThatIsGone` comment already names a rename; no change there.
3. `HistoryPageView.axaml`, the branch badge's `ContextMenu`: a `MenuItem` with
   `Header="{Binding RenameHeader}"`, `IsVisible="{Binding CanRename}"`,
   `Command="{Binding Commands.Rename}"`, `CommandParameter="{Binding}"` and the icon `PencilSimple`.
   It goes in the last action group, right after its `<Separator />` and before
   `Delete "<name>"…`.
4. `RenameBranchDialogView.axaml(.cs)`:
   - Name the box `NameBox`.
   - On `Loaded`, post a focus to it and select the whole name, as `CreateTagDialogView` does, so the
     new name can be typed straight away. This applies to the Branches page's rename too.
5. Tests: a new `tests/Enigma.GitClient.Desktop.UnitTests/HistoryRenameBranchTests.cs`, headless,
   against real git, with the helpers of `HistoryGroupedBadgeTests`:
   - the badge of a local branch shows *Rename "topic"…*, with the glyph `PencilSimple`, bound to
     `Commands.Rename` with the branch as parameter, and placed right before *Delete "topic"…*;
   - a remote-only badge (`origin/x` with no local branch on that commit) does not show it, and
     `Commands.Rename.CanExecute` is `false` for it;
   - confirming the dialog with a new name renames the branch in git (`git branch --list`), and the
     reloaded history draws the new name and not the old one;
   - confirming the current branch's rename keeps HEAD on it under its new name;
   - *Cancel* renames nothing;
   - the dialog's name box has the focus with the whole name selected once it is shown.
6. Documentation sweep (§7): the README's branch-management and badge lines, if now factually
   incomplete.

## Acceptance criteria

- A local branch's badge menu offers *Rename "<name>"…* (glyph `PencilSimple`), just before
  *Delete "<name>"…*. A remote badge does not offer it.
- Choosing it opens the *Rename branch* content dialog, with the name focused and selected. *Rename*
  is enabled only for a valid, new, untaken name. Confirming renames the branch and the history shows
  the new name; *Cancel* changes nothing; a git failure is reported in the info bar.
- The Branches page's rename keeps working through the same dialog.
- The build has zero warnings (AVLN included). The Core unit and integration suites are green. The
  new Desktop tests compile; they are not run on this machine (house rule).

## Out of scope

- Renaming a remote branch (push the new name, delete the old one).
- Carrying the merge source over to the new name (it is forgotten, as today).
- Renaming the upstream along with the branch, or changing `IBranchOperations`.
- A rename item on the commit line's own menu.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| New dialog or the existing one | Reuse `IBranchOperations.RenameAsync` and its dialog | It already validates, writes under the lock and reports failures; its contract says the graph and the Branches page must ask the same questions | A History-specific dialog: two rename flows that could drift |
| Which badges offer it | Local branches, the current one and one checked out in another worktree included | `git branch -m` renames local refs and updates every worktree's HEAD; the Branches page allows the same | Hiding it for the current branch: git handles it, and renaming the current branch is the common case |
| Remote badges | Hidden, not greyed | A remote-tracking ref cannot be renamed with `git branch -m`; Pull/Push are hidden there the same way | Greyed out: suggests it could work |
| Wording, glyph, place | `Rename "<name>"…`, `PencilSimple`, right before `Delete "<name>"…` | The badge names its branch in every header; the glyph is the Branches page's; rename and delete both change the ref itself | `Rename…` without the name: ambiguous on a line with several badges |
| Focus when the dialog opens | Name box focused, whole name selected | Opened from a menu, the focus stays behind the dialog; the tag and squash dialogs already do this | No focus: the reader has to click the box before typing |
| Merge source that is renamed | Forgotten on reload, as today | Carrying it over needs `RenameAsync` to return the new name, an interface change beyond the request | Changing `IBranchOperations` |
| The line's own menu | Not changed | Per-branch actions live on the badge; the line's menu carries only merge-source items per branch | A *Rename* per branch in the line menu |
| Tests | Headless Desktop tests, compiled not run here | House rule for this Windows machine; the release runbook runs them on Linux | Running the Desktop suite here |
