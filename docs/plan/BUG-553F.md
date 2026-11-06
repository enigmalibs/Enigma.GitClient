# BUG-553F — Disabled menu items grey their icon

**Status:** DONE
**Type:** BUG
**Branch:** `bugfix/bug-553f-disabled-menu-icons`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Objective

In a context menu, a disabled item's title is greyed by the theme but its icon keeps the enabled
colour, so the item looks half-enabled. A disabled item's icon takes the same grey as its title — in
the History's line and badge menus, as the report asks, and so in every menu of the application.

## Context & constraints

- Menu icons are `ei:Icon` controls with the `menu` class (`Themes/Styles.axaml`: `ei|Icon.menu`,
  16 px), set as `MenuItem.Icon` — in XAML (the badge menus in `HistoryPageView.axaml`, the tags,
  remotes, worktrees and changed-files menus) and through an item template for the History line menu
  (`ContextMenu.ItemContainerTheme` → `Icon` `<Template>`).
- `ei:Icon.Foreground` inherits `TextElement.Foreground`; FluentTheme's `MenuItem:disabled` greys the
  header presenter (and the gesture text and chevron) with `MenuFlyoutItemForegroundDisabled`, but
  not the icon presenter, so the icon keeps the item's normal foreground.
- Whether the icon inherits from the `MenuItem` (a logical child) or from the template's icon
  presenter decides which selector reaches it: check in a test.

## Steps

1. `Themes/Styles.axaml`, next to the menu rules: a disabled menu item's icon takes the brush the
   theme gives a disabled item's header — `MenuItem:disabled ei|Icon.menu` (or the template part that
   reaches it), `Foreground` = `{DynamicResource MenuFlyoutItemForegroundDisabled}` — with a comment
   saying why. Stated on the icon so it beats the inherited value; no local `Foreground` on any menu
   icon may block it (check the menus).
2. Tests (headless, `MenuIconTests` or a new `DisabledMenuIconTests`): open a History line menu and a
   branch badge menu in a state where an item is disabled (e.g. Push with no remote, or a command whose
   `CanExecute` is false); a disabled item's icon foreground equals its header presenter's foreground,
   an enabled item's icon does not take the disabled brush; both themes.

## Acceptance criteria

- A disabled item's icon is drawn in the same colour as its title, in both themes; an enabled item's
  icon is unchanged.
- The History line and badge menus are covered by tests.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- Which items are disabled, and menu layout.
- Icons on toolbar buttons (they already grey when disabled).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Item type | BUG | The icon contradicts the item's state — a visual defect, not a new capability | FEATURE |
| Scope | One style for every menu in the application, tested on the History menus | The same defect exists wherever an item can be disabled; one rule is simpler than per-view rules and cannot drift | History menus only |
| The grey | FluentTheme's `MenuFlyoutItemForegroundDisabled`, the header's own disabled brush | The draft asks the icon to match the title exactly, in both themes | `EnigmaForegroundSecondaryBrush` (close, but not the same colour); opacity (fades rather than greys) |
