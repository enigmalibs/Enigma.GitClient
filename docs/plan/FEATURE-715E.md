# FEATURE-715E — Escape clears the History search

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-715e-search-escape`
**Run:** feature/2026-10-23-search-escape-profile-cards

## Objective

While the History page's search box has the keyboard focus, Escape clears the search, as the box's
clear button (`ClearSearch`) does.

## Context & constraints

- **The box** is `SearchBox` in `Views/Pages/HistoryPageView.axaml`, at the end of the command bar.
  Its clear button binds `HistoryPageViewModel.ClearSearchCommand`, which empties `SearchText` and can
  run only while there is text.
- **The keys today** (`HistoryPageView.axaml.cs`):
  - `OnSearchKeyDown` tunnels on the box and handles Enter and Shift+Enter (next and previous match).
  - `OnPageKeyDown` tunnels on the whole page and owns Escape. It stops a badge drag, or puts the
    diffs away while they are open, unless a dialog is open over the page.
  - A tunnelling handler on the page runs before one on the box. Escape typed in the box therefore
    reaches the page's handler first, and today it closes the diffs when they are open.
- **The diffs and the bar.** The diff view covers the graph, not the command bar, so the box can have the
  focus while the diffs are open.
- **Baseline** on `3aac49c`: build 0 warnings; 3455 tests, 3452 passed, 3 skipped.

## Steps

1. `HistoryPageView.axaml.cs`, `OnPageKeyDown`:
   - Order of precedence:
     1. a badge drag is stopped;
     2. then, while the search box has the keyboard focus and the search can be cleared, the search is
        cleared through `ClearSearchCommand`, the key is handled, and the focus stays in the box;
     3. then the diffs are put away, as today.
   - With an empty box, Escape goes on to the existing behaviour.
   - Update the XML doc of the handler and the box's comment in the XAML (Enter, Shift+Enter, Escape).
   - The clear button's tip names its key: *Clear the search (Esc)*, as the arrows name theirs.
2. Tests (`HistoryPageTests`, beside `TheSearchBox_StepsOnEnterAndShiftEnter_AndCarriesItsArrows`):
   - With text in the focused box, Escape empties `SearchText`. The matches and the counter are
     cleared, and the focus stays in the box.
   - With the diffs open and text in the focused box, the first Escape clears the search and leaves
     the diffs open. The second Escape puts them away.
   - Escape typed in an empty box changes nothing, and throws nothing.
   - With the focus on the graph, Escape does not clear the search.

## Acceptance criteria

- With the focus in the search box, Escape clears the search exactly as the clear button does.
- Escape anywhere else keeps its current meaning, and in an empty box it still closes the diffs.
- Zero warnings; the whole suite is green.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where Escape clears the search | Only while the box has the keyboard focus | What was asked; Escape on the graph belongs to menus, tips and the diffs | Anywhere on the page: would steal the diffs' Escape |
| Escape in an empty box | Goes on to the page's existing Escape | Nothing to clear, so nothing is swallowed | Always handled: the diffs could no longer be closed from the box |
| Diffs open, box focused with text | Clear first; the next Escape closes the diffs | The focus says what the reader is working on; one key, one step back | Close the diffs first: the box would keep text the reader asked to clear |
| The focus after clearing | Stays in the box | Typing starts the next search at once, as a find bar does | Back to the graph: a second search would need a click |
| Where the code goes | In the page's tunnelling handler, before the diffs | That handler sees the key first. Handling it on the box would come too late, the diffs having already closed | A handler on the box with `handledEventsToo`: two handlers fighting over one key |
| The button's tip | *Clear the search (Esc)* | The arrows' tips name their keys; this one now has one | No change: the key would be undiscoverable |

## Out of scope

- Escape in the app's other filter boxes (branches, tags, changed files, repositories). They keep
  their behaviour; a follow-up could give them the same key.
