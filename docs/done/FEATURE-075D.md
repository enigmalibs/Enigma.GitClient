# FEATURE-075D — Rename a branch from its badge

**Item:** FEATURE-075D — Rename a branch from its badge
**Branch:** `feature/feature-075d-rename-from-badge`
**Run:** feature/2026-10-27-rename-branch-badge

## Summary

A local branch's badge on the History page now offers *Rename "<name>"…* (glyph `PencilSimple`),
right before *Delete "<name>"…*. It goes through `IBranchOperations.RenameAsync`, the Branches page's
own flow:

- the *Rename branch* content dialog, with *Rename* / *Cancel*;
- *Rename* enabled only for a valid, new, untaken name;
- the write under the repository lock, and a *Could not rename "x"* bar on failure.

The history reloads after a rename, and the badge carries the new name. A remote badge does not
offer the item.

The dialog now opens with the caret in the name box and the current name selected, so the new name
can be typed straight away. The Branches page's *Rename…* benefits too.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: the
  `HistoryBranchCommands.Rename` parameter; `CanRename`, `RenameHeader`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: the command,
  `OnRenameBranchAsync`.
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the badge's menu item.
- `src/Enigma.GitClient.Desktop/Views/Dialogs/RenameBranchDialogView.axaml(.cs)`: `NameBox`, focus
  and select-all on load.
- `docs/roadmap.md`, `docs/plan/FEATURE-075D.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryRenameBranchTests.cs`
- `docs/done/FEATURE-075D.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where `Rename` sits in `HistoryBranchCommands` | Last, optional, `null` by default | The record's optional commands (`Copy`, `CreateWorktree`, `Worktrees`) are last and hide their item without it; one call site to change |
| `NotifyBranchCommands` | Not extended | `CanRename` depends only on the badge's kind, which never changes while it is drawn; `CreateWorktree` is not in it either |
| The handler's guard | `branch is not { CanRename: true }` | A remote branch reaching the command by another route still renames nothing |
| Selecting the name on open | `SelectAll()` after `Focus()`, posted on `Loaded` | The tag dialog's pattern; typing replaces the old name, an arrow key keeps it to edit |

## Deviations & follow-ups

- **No deviation from the plan.**
- **README unchanged** (§7 sweep): *Branch management — create, rename, delete, set upstream,
  checkout* is still true; no other line lists the badge menu's items. The release (FEATURE-7CBA)
  names the new item in its notes and callout.
- **Pre-existing, Windows-only Core failure:**
  `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`,
  recorded by FEATURE-839A and the items before it. A test defect under DPAPI, which passes on
  Linux. This dev changes no Core file (`git diff bfd611c -- src/Enigma.GitClient.Core tests/Enigma.GitClient.Core.*`
  is empty).
- **Desktop suite not run here** (house rule): the six new tests have never run. Run them on Linux
  before tagging (the release runbook does).
- A renamed merge source is forgotten on reload, as before (out of scope, see the plan).
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors** (AVLN included); the
  Desktop tests compile.
- `dotnet test --project tests/Enigma.GitClient.Core.UnitTests --no-build`: **1392 total, 1389
  passed, 1 failed, 2 skipped**. The failure is the pre-existing Windows-only `TokenStoreTests` one
  above; the skips are the two DPAPI-inapplicable key-mode checks.
- `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests --no-build`: **414 total,
  412 passed, 0 failed, 2 skipped** (file modes on Windows), in 4 min 24 s.
- `Desktop.UnitTests`: compiled, **not run** on this Windows machine (house rule). Added, in
  `HistoryRenameBranchTests`:
  - `ALocalBranchsBadge_OffersRename_RightBeforeDelete`
  - `ARemoteBranchsBadge_DoesNotOfferRename`
  - `ConfirmingTheDialog_RenamesTheBranch_AndTheHistoryDrawsTheNewName`
  - `RenamingTheCurrentBranch_KeepsHeadOnIt`
  - `CancellingTheDialog_RenamesNothing`
  - `TheRenameDialog_OpensWithTheWholeNameSelected`
- Fix budget: 0 cycles used.
