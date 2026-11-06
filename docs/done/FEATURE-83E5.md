# FEATURE-83E5 — Badge menus: the badge's items only

**Item:** FEATURE-83E5 — Badge menus: the badge's items only
**Branch:** `feature/feature-83e5-badge-menus-own-items`
**Run:** feature/2026-11-06-dialog-focus-badge-menus

## Summary

A right-click on a branch or a tag badge of the History opens the badge's own menu again, and only
that.
- Branch badge: check out, worktree, merge, pull, push, rename, delete, copy the name.
- Tag badge: push, delete, copy the name.

The line's items that FEATURE-F452 appended under them made the menu too long to use, and they are
about the commit, not the badge. They stay on the line's own menu:
- show what it changed, commit details;
- create a branch / worktree / tag here, check out this commit;
- reset, revert, squash;
- the other branches' merge items;
- open on the host, copy the hash.

- `HistoryPageView.OnContextRequested` only asks the line to rebuild its menu, as before F452.
- `Views/BadgeMenus.cs` and `CommitRowViewModel.BadgeMenuEntries` are deleted. No caller is left; the
  49 lines F452 added to the ViewModel are exactly the ones removed.
- `BadgeLineMenuTests` (7) is replaced by `BadgeOwnMenuTests` (4).

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/BadgeOwnMenuTests.cs`
  - `docs/done/FEATURE-83E5.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-83E5.md`: statuses.
- Deleted:
  - `src/Enigma.GitClient.Desktop/Views/BadgeMenus.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/BadgeLineMenuTests.cs`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The `using Enigma.GitClient.Desktop.Views;` F452 added to `HistoryPageView.axaml.cs` | Removed | Only `BadgeMenus` needed it, and it is the view's parent namespace anyway |
| What the tests prove | The menus end with "Copy branch name" / "Copy tag name" and carry none of the line's items; a second request changes nothing; the line's own menu keeps them all | The request's three points, against real git as before |

## Deviations & follow-ups

- The README's "What's new in 6.16" banner and the 6.16.0 release notes say a badge's menu offers the
  line's items. That was true of 6.16, which shipped it, so they are left as written. The next release
  item should say the items are gone again.
- The test infrastructure on this machine is unchanged: see `docs/done/FEATURE-0A3D-PHASE01.md`.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-0A3D-PHASE01.md`.
- **This dev**, `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 2214 run, 2176 passed, 34 failed, 4 skipped. The
  Desktop host crashes at about the same point as in the baseline, and every failure is in the
  baseline's list; **none is new**.
- **Test bodies, per class.** Run on scratch copies of the baseline and of this dev, with only the
  copies' test cleanup patched; the repository is untouched.
  - Classes: `MenuIconTests`, `HistoryGroupedBadgeTests`, `HistoryRenameBranchTests`,
    `HistoryCopyTests`, `HistoryMergeSourceTests`, `HistoryDeleteBothTests`,
    `HistoryStackedBadgesTests`, `WholeLineMenuTests`, `ContextMenuWidthTests`,
    `HistoryBadgeOrderTests`, `RemoteTagsTests`, `HistoryPageTests`.
  - Result: 194 tests; the same 2 failures on both, environmental:
    `HistoryPageTests.RefColumn_GrowsWithTheLongestBadgeAndStopsAtItsMaximum` and
    `RefColumn_StillSeedsItselfAndStillStopsAtItsMaximum`.
  - `BadgeOwnMenuTests`: **4/4 passed**.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README line 78 ("a branch from its badge's menu") stays true.
- The 6.16 banner (line 14): see *Deviations & follow-ups*. No edit.
