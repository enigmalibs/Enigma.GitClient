# FEATURE-47AE — A lighter History command bar

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-23-history-bar-polish

## Objective

The History page's command bar (FEATURE-D977), made lighter:

1. **No theme switch and no About on the bar.** Both are already on the Settings page: the theme
   preference, and the About button.
2. **A narrower search box.**
3. **Clearer groups.** The separators between the centred command groups get more room on their left
   and on their right, so the four groups read as four.

## Context & constraints

- **The bar** is `Views/Pages/HistoryPageView.axaml`, `CommandBar`. It is laid out by `CommandBarPanel`
  in three slots:
  - start: the History title and its chips;
  - centre: `Commands`, twelve named commands in four groups, 2 px apart, with three inline separators
    `Border Width="1" Margin="6,8"`;
  - end: `CommandBarEnd`, a `Grid` (`*,Auto`, `MinWidth="360"`, `MaxWidth="600"`) holding the search
    block (`SearchBox`, the 72 px match counter, three 30 px toolbar buttons). After them come a
    separator (`Margin="6,6"`), `SwitchTheme` and `OpenAbout`.
- **Widths today.** On a wide bar the end is 600 px and the box about 345 px. On a compact bar the end
  is at its minimum (360) and the box about 100 px.
- **The compact threshold.** `CommandBarPanel` goes `:compact` when the start, the full centre and the
  end's `MinWidth` no longer fit. Today the named commands need about 1350 px, and the window is compact
  at 1280 px.
- **Who owns theme and About.**
  - `SwitchTheme` and `OpenAbout` bind `MainWindowViewModel.ToggleThemeCommand` and `OpenAboutCommand`
    through the window. No other view binds them.
  - The start window (`RepositoriesPageView`) has its own theme switch and About, from
    `RepositoriesPageViewModel`, over the same `IThemeSwitcher` and `IAboutDialogService`.
  - The Settings page has the theme preference (`SettingsPageViewModel.Theme`) and `OpenAbout`.
- **Tests pinned to today's bar:**
  - `HistoryCommandBarTests`: `EachCommand_…`, `TheCommands_AreAtTheBarsCentre_WithSearchThemeAndAboutOnItsRight`
    and `OnANarrowWindow_…`;
  - `MainWindowShellTests.ToggleThemeCommand_*` (three);
  - `AboutDialogTests.TheRepositoryToolbar_OpensIt`.
- **Baseline** on `aa146ae`: build 0 warnings; 3453 tests, 3450 passed, 3 skipped.

## PHASE01 — No theme switch nor About on the bar

**Status:** TODO
**Branch:** `feature/feature-47ae-phase01-no-theme-about`

### Steps

1. `HistoryPageView.axaml`:
   - remove the end's separator, `SwitchTheme` and `OpenAbout`;
   - the end becomes the search block alone;
   - the clear button gets a name, `ClearSearch`;
   - the comments that place the theme and About on the bar follow.
2. `MainWindowViewModel`:
   - remove `ToggleThemeCommand` and `OpenAboutCommand`;
   - remove the `IThemeSwitcher` and `IAboutDialogService` dependencies and their XML docs.

   The DI container resolves the window view model, so no registration changes.
3. Comments that say both windows carry the switch follow: `ThemeSwitcher.cs`,
   `RepositoriesPageViewModel` (the `about` and `theme` parameters), `RepositoriesPageView.axaml`, and
   the `:compact` comment in `Styles.axaml`.
4. Tests:
   - `HistoryCommandBarTests`:
     - the window's commands no longer list theme and About;
     - the bar has no button named `SwitchTheme` or `OpenAbout`, and no `Palette` or `Info` icon;
     - the search block is right of the commands and ends at the bar's right edge;
     - on a narrow window the search block stays on the bar.
   - The three `MainWindowShellTests.ToggleThemeCommand_*` become `ThemeSwitcherTests`, over the
     `IThemeSwitcher` the start window still uses.
   - `AboutDialogTests.TheRepositoryToolbar_OpensIt` goes. The repository window reaches About through
     Settings, which `TheSettingsPage_OpensIt_…` covers.

### Acceptance criteria

- The History bar has no theme switch and no About; its right part is the search block.
- `MainWindowViewModel` has no theme or About command; the start window's switch and About are unchanged.
- Zero warnings; the whole suite is green.

## PHASE02 — Narrower search, clearer group gaps

**Status:** TODO
**Branch:** `feature/feature-47ae-phase02-search-and-separators`

### Steps

1. `Styles.axaml`: `Border.commandseparator`, the command bar's group separator.
   - It is 1 px wide, with 16 px on each side and 8 px top and bottom, in the subtle border brush.
   - It is the same in compact mode.
2. `HistoryPageView.axaml`:
   - The three separators of `Commands` take the class instead of their inline width, margin and
     brush.
   - `CommandBarEnd.MaxWidth` comes down, so that on a wide bar the box is about as wide as its
     placeholder needs (about 260 px, from about 345). The number is measured, not guessed.
   - `CommandBarEnd.MinWidth` comes down by the width theme and About took, so that on a compact bar
     the box keeps about 100 px.
   - The end's comment follows.
3. Tests (`HistoryCommandBarTests`):
   - on a wide bar the box is narrower than before (≤ 280 px) and its placeholder is not cut;
   - each separator has 16 px of room either side, and the gap between two groups is clearly wider
     than the gap between two commands of a group;
   - the narrow-window test still holds: compact at 1280 px, nothing off the bar, the box ≥ 90 px;
   - widened again, the named commands are back at the centre.

### Acceptance criteria

- On a wide bar the search box is visibly narrower, and its placeholder still reads in full.
- The four command groups are set apart by wider gaps around their separators.
- Compact mode still keeps every command, and the search block, on the bar.
- Zero warnings; the whole suite is green.

## Out of scope

- The start window's theme switch and About (`RepositoriesPageView`).
- The Settings page.
- The command bar's other parts: the start, the commands and their order, the counters.
- Release notes: they are written by the release item.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where theme and About are removed | The History bar only | The draft names the History page; the start window is not one, and keeps its own | Removing them from the start window too (outside the draft) |
| The window's now-unbound commands | Removed from `MainWindowViewModel`, with their two dependencies; their theme tests move to `IThemeSwitcher` | Dead public surface invites the buttons back; the service, still used by the start window, stays tested | Keeping the commands (dead code); dropping the tests (loses the restart and settings-page coverage) |
| How narrow the search box | About as wide as its placeholder, through `CommandBarEnd.MaxWidth`, measured at build time | A size the content justifies: clearly narrower, still readable | A fixed `Width` on the box (it could no longer give way on a narrow bar); an arbitrary halving (cuts the hint) |
| The end's minimum after the removal | Lowered by what theme and About took | The box keeps its compact floor (about 100 px); the freed room goes to the named commands | Keeping 360 (a wider compact box for no reason) |
| Room around a separator | 16 px each side, from one `Border.commandseparator` style, same in compact mode | About twice the gap between two icons of a group, so the groups read apart; one place to tune | Tweaking three inline margins; a different margin in compact mode (two numbers to keep in step) |
| Phases | The removal; then the search width and the separators | One reviewable commit each; the end's width is retuned once, after the removal | One commit; three phases (the separator change alone is too thin) |
