# FEATURE-F452 — The line's items on badge menus

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-f452-badge-menus-line-items`
**Run:** feature/2026-11-05-history-badges-release

## Objective

A right-click on a branch or a tag badge of the History opens the badge's own menu (check out, pull,
push, rename, delete, …), followed by everything the line's own menu offers: show what it changed, the
commit's details, create branch / worktree / tag here, check out this commit, reset, revert, the squash
items, open on the host, copy the hash. A reader no longer has to aim beside the badges to reach them.

## Context & constraints

- **The line's menu** is data: `CommitRowViewModel.MenuEntries` (a list of `HistoryMenuEntry`,
  `"-"` for a separator), bound as the row `Grid`'s `ContextMenu.ItemsSource` with an
  `ItemContainerTheme`. The view asks the row to rebuild it before it opens:
  `HistoryPageView.OnContextRequested`, a tunnelling handler that calls `row.RefreshMenu()`.
- **The badge menus** are written in XAML in the two `DataTemplate`s of `RefStrip` (branch:
  `HistoryBranchViewModel`, tag: `HistoryTagViewModel`), with live `IsVisible` bindings. Many tests
  read them as `menu.Items.OfType<MenuItem>()` (`HistoryRenameBranchTests`, `HistoryGroupedBadgeTests`,
  `HistoryCopyTests`, `MenuIconTests`…). A plain `RefBadgeItem` badge (the stash) has no menu, and a
  right-click on it already opens the line's.
- **Precedent for a menu built in code-behind:** the drop menu (`HistoryPageView.Offer`), with
  `MenuIcon(PhosphorIcon)` for its glyphs.
- The line's entries include, per branch on the line, "Set "x" as merge source" and "Merge "src" into
  "x"". On a badge, the one for that badge's own branch is already among the badge's items.

## Steps

1. `HistoryPageView.axaml.cs`, `OnContextRequested`: when the right-click's source is inside a
   `RefBadge` whose `ContextMenu` is set and whose `DataContext` is a `HistoryBranchViewModel` or a
   `HistoryTagViewModel`, and the line is a `CommitRowViewModel`:
   - refresh the line's menu, as today;
   - remove the items a previous opening appended (each one is marked, e.g. with a `Tag` sentinel);
   - append a `Separator`, then one `MenuItem` per line entry (a `Separator` for `"-"`), with its
     `Header`, `Command`, `CommandParameter` and `MenuIcon(entry.IconKind)` when `HasIcon`.
2. Skip a line entry that repeats a badge item: same `Command` instance and same `CommandParameter`
   as a badge item that is shown. Collapse separators: none twice in a row, none first or last.
3. Put the building in a small `internal static` helper (`BadgeMenus.AppendLineEntries(ContextMenu,
   CommitRowViewModel)`), so a test can call it without a pointer.
4. The badge's own items stay in XAML, unchanged and first.
5. Tests (`tests/Enigma.GitClient.Desktop.UnitTests/BadgeLineMenuTests.cs`, headless, real git):
   - a local branch badge's menu, once requested (`ContextRequestedEvent` raised on the badge), ends
     with the line's items: "Create branch here…", "Create tag here…", "Check out this commit…",
     "Copy short commit hash", each bound to the line's command with the line (or the hash) as
     parameter, after a separator that follows "Copy branch name";
   - a tag badge's menu has them too;
   - "Set "topic" as merge source" appears once on topic's badge menu, not twice;
   - opening the menu twice does not add the line's items twice;
   - every appended item has a glyph (as `MenuIconTests` asks of every menu);
   - the line's own menu is unchanged.
6. Documentation sweep (§7).

## Acceptance criteria

- A branch badge's menu and a tag badge's menu show their own items first, then a separator, then
  the line's items, without a duplicate of an item the badge already shows.
- Every appended item runs the line's command, and has its glyph.
- Opening the menu repeatedly never duplicates the appended items.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- Turning the badge menus into data (`ItemsSource`).
- Changing the line's own menu.
- A menu on the stash badge (a right-click there already opens the line's).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the line's items reach the badge menu | Appended by the view when the menu is requested, from `MenuEntries` | The badge items keep their XAML, their live bindings and the tests that read them; the line's items come from the one place that builds them; the drop menu is the precedent | Rewriting both badge menus as `HistoryMenuEntry` data: every badge-menu test would be rewritten, and their visibility rules moved to code |
| Order | Badge items first, then the line's | The badge is what was clicked; the request adds the line's items to it | The line's first |
| Duplicates | Skip a line entry with the same command and parameter as a shown badge item | "Set "topic" as merge source" twice in one menu is noise | Keeping every line entry |
| Branches other than the badge's | Their line entries stay (e.g. "Set "main" as merge source" on topic's badge) | They are the line's items, which the request asks for | Filtering per-branch entries out |
| Hook | The page's tunnelling `ContextRequested` handler | It already rebuilds the line's menu there, before any menu opens | A `ContextMenu.Opening` handler in XAML: the house has no XAML event handlers |
