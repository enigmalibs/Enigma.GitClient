# BUG-553F — Disabled menu items grey their icon

**Item:** BUG-553F — Disabled menu items grey their icon
**Branch:** `bugfix/bug-553f-disabled-menu-icons`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

A context menu item that cannot be chosen is now grey all over: its glyph takes the brush its title
takes.

- **Cause.** FluentTheme's `MenuItem:disabled` greys the title, shortcut and chevron through their
  template parts. The icon is not a template part: it is the item's `Icon`, an `ei:Icon` whose
  `Foreground` inherits the item's own, so it stayed in the enabled colour. Measured in the dark theme:
  title `#66FFFFFF`, glyph white.
- **Fix.** One style in `Themes/Styles.axaml`: `MenuItem:disabled ei|Icon.menu` takes
  `{DynamicResource MenuFlyoutItemForegroundDisabled}`, the brush the theme gives the title. Every menu
  icon in the application is an `ei:Icon.menu` and a logical child of its item, so the History's line
  and badge menus are fixed, and every other menu with them. An item enabled again takes its colour
  back, since the rule applies only while the item is disabled.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/DisabledMenuIconTests.cs`
  - `docs/done/BUG-553F.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Themes/Styles.axaml`
  - `docs/roadmap.md`, `docs/plan/BUG-553F.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The selector | A descendant rule on the item (`MenuItem:disabled ei|Icon.menu`) | A probe showed the glyph's logical parent is the `MenuItem`, for XAML icons and for the line menu's templated ones alike; a style on the glyph itself beats the inherited value |
| The brush | `MenuFlyoutItemForegroundDisabled` | The test compares the glyph's colour with the title presenter's, in both themes, and they are equal |

## Deviations & follow-ups

- None.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-4F5C-PHASE01.md` (clean).
- **Reproduced before the fix** (a probe over the History's menus, dark theme): "Reset "main" to this
  commit - Soft", "Check out "main"" and "Delete "main"…" were disabled with a `#66FFFFFF` title
  and a white glyph.
- **This dev:** build 0 warnings, 0 errors; tests 3675 run, 3672 passed, 0 failed, 3 skipped.
- **New tests** (`DisabledMenuIconTests`, 2):
  - on the History's line menus and branch/tag badge menus, in the dark and the light theme, every
    shown item's glyph is its title's colour, at least one item of each kind of menu is disabled, and
    no enabled glyph is grey;
  - a stand-alone menu item greys its glyph when disabled and takes its colour back when enabled.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing in the README describes menu colours; no edit.
