# FEATURE-0CDE — History search in its own bar

**Item:** FEATURE-0CDE — History search in its own bar
**Branch:** `feature/feature-0cde-search-bar`
**Run:** feature/2026-11-06-repo-tiles-history-search

## Summary

The History search left the right end of the command bar for a bar of its own, under it:

- **The toggle.** The commands end with a new separator right of Close, then **Search**
  (`MagnifyingGlass`): a command-shaped `ToggleButton` that stands on the selection plate while it is
  on. Its tip names Ctrl+F.
- **The bar.** `SearchBar` is hidden when the page opens and shown by the toggle. It sits under the
  command bar, across the whole page. At its centre are the box (400 px, twice the 200 it had), the
  "match x/y" count and the previous, next and clear buttons, with the same names, tips and keys as
  before.
- **The count** is drawn in `EnigmaForegroundSecondaryBrush`, not the near-background tertiary grey.
- **Keyboard:**
  - Ctrl+F shows the bar and puts the caret in the box, or goes back to the box when the bar is
    already shown. A diff's own Ctrl+F is untouched: the editor handles the key first.
  - Escape backs out one thing at a time: the text, then the bar, then the diffs. When the bar goes,
    the focus returns to the diffs or to the selected line.
- **Hiding the bar clears the search** (`HistoryPageViewModel.IsSearchBarOpen`), so no line stays
  highlighted by a search you cannot see.

The command bar has no end slot any more; `CommandBarPanel` keeps supporting one.

## Files/modules touched

- Created:
  - `docs/done/FEATURE-0CDE.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`, `HistoryPageView.axaml.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
  - `src/Enigma.GitClient.Desktop/Themes/Styles.axaml` (`ToggleButton.segment.command`, compact rule)
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCommandBarTests.cs`, `HistoryPageTests.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-0CDE.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A `ToggleSearchBarCommand` (plan step 1) | None: the toggle's `IsChecked` is bound two-way to `IsSearchBarOpen` | A bound toggle needs no command; Ctrl+F and Escape set the property |
| The toggle's look | The existing `ToggleButton.segment` (frame, selection plate when on, grey when disabled), given the command's padding and width | The segment toggles already are toolbar buttons that can be on; restyling `Button.toolbar` selectors for a subclass would touch every toolbar button |
| The box on a narrow window (plan: "shrinking") | A fixed 400 px | The window's minimum width (900) leaves room for the whole row; a test at a narrow width holds it to that |
| Escape in an empty box with the diffs open | Puts the bar away first; the next Escape leaves the diffs | One layer per Escape, nearest first: the focus is in the bar |
| Where the focus goes when the bar goes | The diffs while they are up, else the selected line (else the first one shown) | The graph's `ListBox` takes no focus in Avalonia 12, so focusing it left the focus on nothing |
| Detecting "the focus was in the bar" | Read after the bar is hidden: no focus, or focus still inside it | Hiding the bar can drop the focus before the page hears of the change |

## Deviations & follow-ups

- `HistoryPageView.MoveFocus` (leaving the diffs) still calls `CommitList.Focus()`, which does nothing
  since the list is not focusable: after Escape leaves the diffs, the focus is on nothing. This is
  pre-existing and left alone; `FocusTheGraph` (added here) is the fix to reuse.

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-4F5C-PHASE01.md` (clean).
- **This dev:** build 0 warnings, 0 errors; tests 3673 run, 3670 passed, 0 failed, 3 skipped.
- **Tests rewritten** (`HistoryCommandBarTests`):
  - the commands in five groups ending with `|`, `Search`;
  - the commands centred, ending with the toggle;
  - the bar hidden until a click on the toggle shows it under the command bar, at full width, with
    the caret in the box, and a second click hides it;
  - the row centred in order, the box 400 px with its placeholder in full;
  - the count's brush;
  - 13 command-shaped buttons;
  - on a narrow window (1000) the bar is compact with the toggle on it, and the search row fits;
  - widened again, the names come back.
- **Tests updated or added** (`HistoryPageTests`):
  - Enter / Shift+Enter and Escape open the bar first;
  - Escape: clear, then the bar, then the diffs;
  - Escape in an empty box puts the bar away and focuses the selected line;
  - Ctrl+F shows the bar and focuses the box, and again with the text selected;
  - Ctrl+F in a diff opens the diff's own search;
  - hiding the bar clears the search.
- **Fix budget:** 0 cycles. The focus issues were found by the new tests during implementation,
  before the first full run.

## Documentation sweep

- README's History bullet ("a search … that highlights what it found … steps through it with its
  arrows or Enter") stays true; no edit.
- `HistoryPageViewModel.MatchSummary`'s summary said "the toolbar"; it now says "the search bar".
