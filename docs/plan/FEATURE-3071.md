# FEATURE-3071 — A narrower navigation rail

**Status:** DONE — see `docs/done/FEATURE-3071.md`
**Type:** FEATURE
**Branch:** `feature/feature-3071-narrower-rail`
**Run:** feature/2026-09-29-rail-clone-history

## Objective

The navigation rail on the left of both windows is too wide: there is too much space to the left and
the right of each item's icon and label. Make it narrower, so that the widest label sits in the item
with the item's own padding and very little more.

## Context & constraints

- Both windows declare the rail with `PaneSize="96"`:
  - `Views/MainWindow.axaml` — History, Changes, (Conflicts, during a merge), Profiles, Settings;
  - `Views/StartWindow.axaml` — Repositories, Profiles, Settings.
- `PaneSize` is the rail's `Width` (Enigma.Avalonia.Desktop 1.1.0, `NavigationView`). The rail has a
  1px right border. Each item is a `ListBoxItem` with `Padding="8"`, centred content, and a
  `NavigationItem` whose template has a `Margin="1"`. Inside it are a 24px icon and an 11px label
  with `TextWrapping="Wrap"`, so a label that does not fit breaks mid-word.
- The item padding is set by `ListBox.Styles` inside the library's own template. Those styles are
  closer to the items than any application style, so the application can't change the padding
  without restyling the library. Only the width is the application's to set.
- The longest label is "Repositories", in the start window. The application draws in Inter
  (`WithInterFont()`, `Program.cs`). The headless test fixture doesn't register Inter, so rendered
  test text uses a fallback face.

## Steps

1. Add one shared width to `Themes/Styles.axaml`: `<x:Double x:Key="NavigationRailWidth">…</x:Double>`.
   Use the narrowest multiple of 4 at which "Repositories" in Inter 11px, plus the item's chrome
   (2 × 1px template margin, 2 × 8px padding, the 1px border), still fits on one line.
2. Both windows: `PaneSize="{StaticResource NavigationRailWidth}"`.
3. Tests (headless):
   - both windows' rail is `NavigationRailWidth` wide, narrower than the old 96;
   - every label of both rails, measured in Inter at the item's font size, fits inside the rail with
     the item's chrome, so none wraps.

## Acceptance criteria

- Both rails are narrower than 96 and the same width, set in one place.
- No rail label wraps or is clipped in the application's font. That covers "Repositories", and
  "Conflicts" when a merge shows it.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Restyling the library's item (padding, icon size, label size).
- A collapsible rail, or labels hidden in favour of icons only.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How much narrower | The narrowest multiple of 4 that keeps "Repositories" on one line | Removes the spare space on both sides without ever wrapping a label | A fixed guess such as 72 (would break "Repositories" mid-word) |
| One width or one per window | One shared resource for both windows | The two windows read as one application; one number can't drift | 72 for the repository window and more for the start window |
| Reduce the item padding too | No | The padding is set inside the library's template and outranks application styles. Changing it means restyling the library | An application style (silently loses), a code-behind override |
| How the test measures | `FormattedText` in Inter from `avares://Avalonia.Fonts.Inter/Assets#Inter` | Tests the face the application actually draws in. The fixture's fallback face is wider and would give a false failure | Registering Inter in the shared fixture (would change every other test's text layout) |
