# FEATURE-83E5 — Badge menus: the badge's items only

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-83e5-badge-menus-own-items`
**Run:** feature/2026-11-06-dialog-focus-badge-menus

## Objective

A right-click on a branch or a tag badge of the History opens the badge's own menu only — check out,
worktree, merge, pull, push, rename, delete, copy the name for a branch; push, delete, copy the name
for a tag. The line's items that FEATURE-F452 appended under them (show what it changed, commit
details, create a branch / worktree / tag here, check out this commit, reset, revert, squash, the
other branches' merge items, open on the host, copy the hash) made the menu too long to use: they are
about the commit, not the badge, and stay on the line's own menu.

## Context & constraints

- FEATURE-F452 (`docs/plan/FEATURE-F452.md`, `docs/done/FEATURE-F452.md`) added:
  - `src/Enigma.GitClient.Desktop/Views/BadgeMenus.cs` — `AppendLineEntries` / `RemoveLineEntries`;
  - `CommitRowViewModel.BadgeMenuEntries(object badge)`;
  - in `HistoryPageView.OnContextRequested`, the branch that appends them on a branch or tag badge;
  - `tests/Enigma.GitClient.Desktop.UnitTests/BadgeLineMenuTests.cs` (7 tests).
- `OnContextRequested` also asks the line to rebuild its own menu (`row.RefreshMenu()`) before any menu
  opens; that stays.
- The badge menus' own items are in XAML (`HistoryPageView.axaml`, the two `RefBadge` templates) and
  are all about the badge (the "Clear the merge source" item shows only on the source's own badge).

## Steps

1. `HistoryPageView.OnContextRequested`: drop the badge branch; keep `row.RefreshMenu()`; update its
   documentation comment.
2. Delete `Views/BadgeMenus.cs` and `CommitRowViewModel.BadgeMenuEntries` (no other caller).
3. Replace `BadgeLineMenuTests.cs` with `BadgeOwnMenuTests.cs` (headless, real git, as before):
   - a local branch badge's menu, once requested (`ContextRequestedEvent` raised on the badge), holds
     only its XAML items — it ends with "Copy branch name", and has none of the line's headers ("Create
     branch here…", "Check out this commit…", "Copy short commit hash"…);
   - a tag badge's menu ends with "Copy tag name", the same;
   - requesting it twice changes nothing;
   - the line's own menu still has its items.
4. Documentation sweep (§7).

## Acceptance criteria

- A branch badge's and a tag badge's menu show their own items only.
- The line's menu is unchanged.
- No dead code left from the appending.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- Changing which of the badge's own items show.
- The release notes and the README's "What's new in 6.16" banner (they describe 6.16, which shipped
  the appended items); the next release item says they are gone.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which items "do not concern" a badge | Every item appended from the line's menu | Each acts on the commit (or another branch), never on the badge's branch or tag; the ones that did were already left out by F452 | Keeping a curated subset (e.g. "Check out this commit…"): still the commit's, and the request is a shorter menu |
| The appending code | Deleted, with its tests | No caller remains; dead code is a maintenance cost | Keeping `BadgeMenus` unused, or behind a switch |
| Keyboard menu (Shift+F10) on a badge | Same as the right-click: the badge's items only | One path (`ContextRequested`) for both | — |
