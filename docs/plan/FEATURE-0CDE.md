# FEATURE-0CDE — History search in its own bar

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-0cde-search-bar`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Objective

The History search leaves the right end of the command bar for a bar of its own, under the command
bar, shown on demand:

- a new **Search** toggle ends the command bar's commands, after a new separator right of Close;
- the search bar is collapsed when the page opens, and the toggle shows and hides it;
- in it, centred: the search box — twice as wide as today — the "match x/y" counter and the previous,
  next and clear buttons;
- the counter is drawn in a legible grey, not the near-background one that looks disabled.

## Context & constraints

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the `CommandBar` border holds a
  `controls:CommandBarPanel` with three slots — `CommandBarStart` (title, chips), `Commands` (the
  centred command buttons, groups split by `Border.commandseparator`, last group Refresh · Close) and
  `CommandBarEnd` (a `Grid` `MinWidth=310 MaxWidth=380`: `SearchBox` (200 px at most), `MatchCounter`
  (`EnigmaForegroundTertiaryBrush`, 11 pt), `PreviousMatch`, `NextMatch`, `ClearSearch`).
- `CommandBarPanel` centres the commands at the bar's true centre and goes `:compact` (commands drop
  their names) when the start, commands and the end's minimum do not fit. With no end slot it centres
  the commands with only the start beside them.
- Code-behind (`HistoryPageView.axaml.cs`): Enter / Shift+Enter in `SearchBox` step through matches;
  Escape in the box clears the search (keeps the focus), and otherwise closes the diffs.
- ViewModel (`HistoryPageViewModel`): `SearchText`, `MatchSummary`, `ClearSearchCommand`,
  `NextMatchCommand`, `PreviousMatchCommand`; rows carry `IsSearchMatch`.
- The diff viewer has its own Ctrl+F search (README); it must keep it.
- Command buttons are `Button.toolbar.command` (icon `command` 24, `commandlabel` under it). Avalonia
  type selectors do not match subclasses, so a `ToggleButton` does not get the `Button.toolbar` look.
- Tests on the current layout: `HistoryCommandBarTests` (order of the commands, search right of the
  commands, 200 px box, compact bar keeps the search on the bar…) and `HistoryPageTests` (search box
  Enter / Shift+Enter / Escape).

## Steps

1. ViewModel: `IsSearchBarOpen` (bool, `false` at construction) and `ToggleSearchBarCommand`. Closing
   it clears `SearchText`. `OpenSearchBar()` (or the command with a parameter) for Ctrl+F.
2. Command bar:
   - drop `CommandBarEnd` from the `CommandBarPanel` (the panel keeps its end-slot logic for any other
     user; check there is none needed);
   - append to `Commands`: a `Border.commandseparator`, then `ToggleSearchBar` — a toggle with the
     command look (icon `MagnifyingGlass`, label "Search", tip "Show or hide the search bar (Ctrl+F)",
     automation name), `IsChecked` bound two-way to `IsSearchBarOpen`. Give `ToggleButton.toolbar` the
     toolbar/command look (the `Button.toolbar` setters, the compact rules) plus a `:checked` state
     (the selection brush plate), in `Themes/Styles.axaml`.
3. The search bar: a `Border` named `SearchBar` docked under `CommandBar` (same surface, bottom border),
   `IsVisible="{Binding IsSearchBarOpen}"`, holding a centred row: `SearchBox` (`Width` 400, shrinking
   on a narrow window, never under the width its placeholder needs), `MatchCounter` (foreground
   `EnigmaForegroundSecondaryBrush`), `PreviousMatch`, `NextMatch`, `ClearSearch` — the names, tips,
   automation names and key handling unchanged.
4. Keyboard (code-behind): Ctrl+F on the page — unless the diff viewer handled it — opens the bar and
   focuses the box (text selected); Escape in a non-empty box clears it (as now); Escape in an empty
   box closes the bar and gives the focus back to the commit list.
5. Opening the bar from the toggle focuses the box.
6. Tests: rewrite the affected `HistoryCommandBarTests` (the commands now end with `|`, `Search`; the
   bar is collapsed on open; the toggle shows it under the command bar; the search row is centred in
   it; the box is 400 px; the counter uses the secondary brush; closing clears the search; a compact
   command bar still holds the toggle); update `HistoryPageTests`' search tests to open the bar first;
   new: Ctrl+F opens and focuses, Escape twice clears then closes, the diff viewer's Ctrl+F still goes
   to the diff.
7. Documentation sweep (§7): README's History search bullet if it names where the box is.

## Acceptance criteria

- The History page opens with no search bar; the command bar ends with a separator and the Search
  toggle after Close.
- The toggle (and Ctrl+F) shows a bar under the command bar with the box (400 px), the counter and the
  three buttons centred; the toggle shows it is on.
- Hiding the bar clears the search; Enter, Shift+Enter and Escape behave as before inside the box.
- The counter reads in the secondary foreground.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- What the search matches, and how matches are highlighted.
- Remembering the bar's state between sessions or windows.
- The diff viewer's own search.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| "A dedicated toolbar behind the buttons" | A full-width bar directly under the command bar, pushing the graph down while shown | "Behind" read as "after / below": a toolbar row under the main one is the common find-bar shape and keeps the graph unobscured | A floating panel over the graph; a popup under the toggle |
| The toggle's place and look | A command button (icon + "Search") at the end of the centred commands, after a new separator right of Close | Exactly the draft's "right-most, after a new separator on the right of close"; the same look as its neighbours | An icon-only button at the bar's right edge (not "after a separator right of Close") |
| Toggle control | `ToggleButton` styled as the command buttons, `:checked` drawn with the selection plate | Exposes its on/off state to automation and to the eye | A `Button` with a bound class (state invisible to accessibility) |
| Remember the state | No — collapsed whenever a History page opens | The draft says collapsed by default; nothing asks to remember | Persisting in settings |
| A search left active when the bar is hidden | Hiding the bar clears it | Highlighted lines with no visible search, and no way to clear it, would be a trap | Keep the text and highlights; keep the text but suspend the highlights |
| Keyboard | Ctrl+F opens the bar and focuses the box; Escape in an empty box closes it | A hidden box needs a keyboard way in and out; Ctrl+F is the universal find key, and the diff viewer's own Ctrl+F is untouched since it handles the key first | No shortcut (keyboard users lose the search) |
| Box width | 400 px (twice the current 200), shrinking on a narrow window | The draft's "twice as long"; the bar has the whole width | A fixed 400 that would overflow a narrow window |
| Counter colour | `EnigmaForegroundSecondaryBrush` | The house's legible quiet role; tertiary is the near-background grey the draft calls disabled-looking | Full foreground (competes with the box) |
| The `CommandBarPanel` end slot | Left empty (the control keeps supporting it) | The control is generic and tested; the History bar simply has no end slot now | Deleting the end-slot logic |
