# FEATURE-D977-PHASE01 — Bigger strip and head badge

**Item:** FEATURE-D977 — Repository strip and command bar, PHASE01 — Bigger strip and head badge
**Branch:** `feature/feature-d977-phase01-repository-strip`
**Run:** feature/2026-10-23-strip-and-command-bar

## Summary

The repository strip — the window's top bar — draws the open repository larger, and where HEAD is in
the badge the history gives the checked-out branch.

- **The name** is 18 px SemiBold (it was the default 14).
- **The icon** before it, and the tree a linked worktree adds after it, take a new role on the icon
  scale, **`title` = 24** (they were `header`, 20): the open repository's name is the window's title,
  a step above a page's.
- **The branch badge** is the graph's own `RefBadge`, declared as a history line declares the branch HEAD
  is on (`Kind="LocalBranch"`, `IsCurrent="True"`). It is the same control with the same `head` class:
  - `RefBadgeHeadBrush` behind white text, in both themes;
  - 13 px text, padding 9,3;
  - the check before the branch icon;
  - its tooltip and accessible name.

  It cannot drift from the lines' badge, because it is the same control.
- **A detached HEAD** shows no branch badge, because the blue means "the checked-out branch" in the
  graph. The warning pill it already had now says where HEAD is (`detached at abcdef1`), not
  `detached HEAD`. That replaces the two pills it used to show.
- `MainWindowViewModel.IsOnBranch`: a repository is open and HEAD is not detached (unborn included).
  It is notified with the repository and with HEAD.

The strip's buttons are unchanged in this phase; PHASE03 moves them to the History bar.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryStripTests.cs`: 5 cases.
  - The name is 18 SemiBold, the icon and the worktree tree are `title` and 24.
  - The badge is the history's checked-out-branch badge: same brush (`RefBadgeHeadBrush`), same
    foreground, same text size, same height and width. It is checked in Dark and in Light.
  - A detached HEAD has no badge, and the pill reads `detached at abcdef1`.
  - `IsOnBranch` follows the repository and HEAD: none, unborn, closed, detached.
- `docs/done/FEATURE-D977-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`: the `title` role and its line in the scale's
  comment; `header` is now "beside the name of a page".
- `src/Enigma.GitClient.Desktop/Views/MainWindow.axaml`: the strip's left part (`RepositoryIcon`,
  `RepositoryTitle`, `HeadBadge`, `DetachedHeadPill`), and the `gitcontrols` namespace.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: `IsOnBranch`.
- `tests/Enigma.GitClient.Desktop.UnitTests/ShellRenderTests.cs`: the scale gains `title` = 24, which
  must be bigger than `header`.
- `docs/roadmap.md`, `docs/plan/FEATURE-D977.md`: statuses.

## Deviations & follow-ups

- **No deviation from the plan.**
- The strip is about 6 px taller: a 24 px icon instead of 20, and 18 px text.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the strip's badge is declared | A `RefBadge` with literal `Kind`/`IsCurrent`, bound `Text` | Exactly what a history line's template gives the checked-out branch, without a second view model |
| The detached pill's text size | 13 px, the badge's | The pill replaces the badge in the same slot; one height either way |
| The spacing between the strip's items | 10 (was 8) | Scaled with the larger name and icon |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3436 total, 3433 passed, 3 skipped, 0 failed (+6: the
  5 cases above, and the scale's new `title` row).
  - Cycle 1 fixed a nullable warning in the new test file: the first build failed on `CS8602`.
- Looked at by eye in the rendered `main-window-repository-open.png` snapshot. The larger folder icon
  and name are followed by the blue `✓ ⑂ main` badge.
