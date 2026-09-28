# FEATURE-5860-PHASE04 — Icons on the important operations

**Item:** FEATURE-5860 — Context menus that do more
**Branch:** `feature/feature-5860-phase04-menu-icons`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

Every actionable item of every context menu now has a Phosphor glyph, one per kind of action. The glyph
is an `ei:Icon` in `MenuItem.Icon` with the new `menu` class (16 px). It inherits the item's foreground,
so it follows the theme and the disabled state with no brush of its own.

| Action | Glyph |
|---|---|
| Show what it changed | `GitDiff` |
| Check out (branch, tag, commit) | `SignIn` |
| Create branch / tag here | `GitBranch` / `Tag` |
| Reset soft / hard | `ArrowUUpLeft` |
| Set as merge source / clear it | `Target` / `X` |
| Merge (every merge item, the drop menus' too) | `GitMerge` |
| Fast-forward only | `FastForward` |
| Pull / push / fetch | `ArrowDown` / `ArrowUp` / `ArrowsDownUp` (the main toolbar's) |
| Delete, remove, delete stash | `Trash` |
| Discard (changed file) | `TrashSimple` (the Changes page's discard button) |
| Stage / unstage | the panel's own button glyph (`Plus` / `Minus`) |
| Stash all changes / apply / pop | `Archive` / `TrayArrowUp` / `ArrowCounterClockwise` |
| Select in the history | `Crosshair` |
| Show / hide in the history | `Eye` / `EyeSlash`, following the item |
| Rename, edit | `PencilSimple` |
| Set upstream | `LinkSimple` |
| Open on the host | `ArrowSquareOut` |
| Open file / show in file manager | `FileText` / `FolderOpen` |
| Copy (hash, name, path) | `Copy` |

Where the icons live:

- **The history line's menu** is data. `HistoryMenuEntry` carries an optional `Icon` (plus `HasIcon` and
  `IconKind`), and the container theme draws it through a `<Template>` setter, so every item gets a
  control of its own.
- **The drop menus built in code** (history and branches dialog) use `HistoryPageView.MenuIcon`.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/MenuIconTests.cs` — 4 tests:
  - every entry of every kind of history line has a glyph;
  - the opened line menu draws them;
  - the badge menus;
  - the branches, tags, remotes and Changes pages' menus, the stash and file rows included.

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryBranchViewModel.cs` — `HistoryMenuEntry.Icon`,
  `HasIcon`, `IconKind`.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` — a glyph on each of the 15 entries.
- `src/Enigma.GitClient.App/ViewModels/Panels/ChangedFilesPanelViewModel.cs` —
  `ChangedFileRowActions.SecondaryIcon`.
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml(.cs)`:
  - the entry icon template;
  - the badge menus' glyphs;
  - `MenuIcon`, and the drop menu's glyphs.
- `src/Enigma.GitClient.App/Views/Pages/BranchesPageView.axaml(.cs)`, `TagsPageView.axaml`,
  `RemotesPageView.axaml`, `ChangesPageView.axaml`, `Views/Panels/ChangedFilesPanelView.axaml` — glyphs.
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — `ei|Icon.menu`.
- `tests/Enigma.GitClient.App.UnitTests/HistoryDragMergeTests.cs`, `BranchesPageTests.cs` — the drop
  menus' glyphs.
- `docs/roadmap.md`, `docs/plan/FEATURE-5860.md` — statuses. The item is done.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Which items | Every actionable item | A menu with glyphs on some rows and not others reads as broken, and nearly every item is an operation |
| Reuse vs new glyphs | The glyph the app already uses for the same action elsewhere (toolbar, row buttons) | One action, one picture |
| The icon's size | 16 px, a class of its own | The size Fluent's menu icon column is laid out for |
| Reset vs pop | `ArrowUUpLeft` vs `ArrowCounterClockwise` | Two different actions, two glyphs |

## Deviations & follow-ups

- Fix cycle 1, test only: the new Changes-page icon test stashed a clean tree, so it waited for a stash
  that did not exist. It now writes an untracked file first.
- Line endings: no CRLF churn.

## Documentation sweep

No user-facing document describes menu glyphs. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors, no `AVLN` XAML warning.
- `dotnet test --solution Enigma.GitClient.slnx`: **2445 passed**, 0 failed (4 new, 2 extended), after
  one fix cycle (above).
