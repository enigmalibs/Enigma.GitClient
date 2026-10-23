# FEATURE-715E — Escape clears the History search

**Item:** FEATURE-715E — Escape clears the History search
**Branch:** `feature/feature-715e-search-escape`
**Run:** feature/2026-10-23-search-escape-profile-cards

## Summary

While the History page's search box has the keyboard focus, Escape clears the search, as its clear
button does. The text, the matches and the counter go, and the focus stays in the box, so the next search
is typed at once.

- **Where.** In the page's tunnelling Escape handler (`HistoryPageView.OnPageKeyDown`), which sees the
  key before anything inside the page. The order is:
  1. a badge drag is stopped;
  2. then, with the focus in the box and something to clear, the search is cleared through
     `ClearSearchCommand`;
  3. then the diffs are put away, as before.
- **With the diffs open.** The bar is not under the diffs, so the box can have the focus while they are
  open. The first Escape clears the search and the second leaves the diffs.
- **An empty box** has nothing to clear and lets the key through, so Escape there still closes the
  diffs.
- **Elsewhere** — the graph, the details panel, the diffs — Escape keeps its meaning and leaves the
  search alone.
- The clear button's tip names its key, *Clear the search (Escape)*, as the arrows' tips name theirs.

## Files / modules touched

**Created**

- `docs/done/FEATURE-715E.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`: `OnPageKeyDown` clears the
  search first; its XML doc.
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the box's comment names Escape;
  the clear button's tip.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`, four new tests:
  - `Escape_InTheSearchBox_ClearsTheSearch_AndKeepsTheFocusThere`: text, matches and counter
    cleared, the command no longer runnable, the focus still in the box, and the button's tip;
  - `Escape_InTheSearchBox_ClearsTheSearchBeforeItLeavesTheDiffs`: the first Escape clears, the
    second closes the diffs;
  - `Escape_InAnEmptySearchBox_ChangesNothing`: the selection kept, no diffs opened, the focus kept;
  - `Escape_OnTheGraph_LeavesTheSearchAlone`.
- `docs/roadmap.md`, `docs/plan/FEATURE-715E.md`: statuses.

## Deviations & follow-ups

- **The tip.** The plan said *Clear the search (Esc)*. The start window's filter box already clears on
  Escape, and its tip says *Clear the filter (Escape)*. The History tip takes the same word.
- **The start window does it with a `KeyBinding`** on its box. Here a binding on the box would come too
  late: the page's tunnelling handler would already have closed the diffs. So the page's handler,
  which owns Escape on this page, does it.
- **Follow-up.** The other filter boxes (branches, tags, changed files) do not clear on Escape yet.
  The start window's does.
- Documentation sweep:
  - The README says the search "steps through it with its arrows or Enter". That is still true, so
    nothing was edited.
  - `RELEASENOTES.md` is written by the release item.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The key's name in the tip | *Escape*, not *Esc* | The start window's filter tip already says *(Escape)* |
| A `KeyBinding` on the box, as the start window does | No: the page's tunnelling handler | The page's handler sees Escape first and would close the diffs before a binding on the box ran |
| What "can be cleared" means | `ClearSearchCommand.CanExecute` | Exactly when the clear button is enabled, so the key and the button never disagree |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3459 total, 3456 passed, 3 skipped, 0 failed. No
  fix cycle was needed.
  - That is +4 over the baseline's 3455: the four Escape tests.
