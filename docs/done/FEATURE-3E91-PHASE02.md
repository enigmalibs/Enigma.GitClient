# FEATURE-3E91-PHASE02 — Step through the matches

**Item:** FEATURE-3E91 — Search SHAs, authors, step through (PHASE02 of 2)
**Branch:** `feature/feature-3e91-phase02-match-navigation`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

The history's search now shows what it found on the **right** of the box, as `match xx/yyy`, with
up and down arrows that go to the previous and the next match.

- **`HistoryPageViewModel`:**
  - `NextMatchCommand` / `PreviousMatchCommand` select the next or previous matching line, starting
    from the selected one, and wrap around within the loaded lines. With nothing selected, down goes
    to the first match and up to the last. Both are enabled while something matches.
  - Selecting is going there: the list brings the line into view and the details panel follows, as a
    click would.
  - `MatchPosition` is the selected line's 1-based place among the matches, or 0 when the selected
    line is not one of them.
  - `MatchSummary` reads `match 3/12`, or `match –/12` when the selection is not a match. It still
    reads `no match` when nothing matches, and is empty when nothing is searched for. It follows the
    selection, the search, and rows loaded or redrawn.
- **`HistoryPageView.axaml`:** the order is now box, counter (`MatchCounter`), up arrow
  (`PreviousMatch`, `ArrowUp`), down arrow (`NextMatch`, `ArrowDown`), clear. The arrows have tooltips
  that name their keys, and accessible names. The group's maximum width grew from 440 to 520.
- **`HistoryPageView.axaml.cs`:** in the box, Enter goes to the next match and Shift+Enter to the
  previous one. The focus stays in the box, so the next press keeps stepping.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`:
  - the two pinned summaries (`3 matches`, `1 match`) are now `match –/3` and `match –/1`;
  - five new tests:
    - stepping down and up with wrap-around, and the counter at each step;
    - stepping from a line that is not a match;
    - up with nothing selected goes to the last match;
    - no match means no stepping;
    - in the realised view: the arrows' commands, icons and names, the counter on the right of the
      box, and Enter / Shift+Enter.
- `docs/roadmap.md`, `docs/plan/FEATURE-3E91.md`: statuses. The item is `DONE` with its last phase.

**Created**

- `docs/done/FEATURE-3E91-PHASE02.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Arrows and counter while nothing is searched for | Always laid out. The arrows are disabled and the counter is empty | Otherwise the box would shift left the moment the first letter is typed, because the group is right-aligned. A find bar should not move under the cursor |
| How Enter is caught | A tunnelling `KeyDown` handler on the box, in the code-behind | It is guaranteed to see Enter before the `TextBox` does. A XAML `KeyBinding`'s ordering against the box's own key handling could not be checked, with the Desktop tests not run this session |
| What "previous" starts from with nothing selected | The last match | The mirror of "next goes to the first", as in every find bar |

## Deviations & follow-ups

- The plan said the summary and the arrows "show while something is searched for". They are always
  laid out instead (see above). The visible behaviour is the same: nothing reads and nothing is
  clickable until a search finds something.
- **Not run:** the new and the updated Desktop tests are compiled but were not run, because you asked
  to skip the Desktop tests this session.
- README: the *Features* line on the search is updated with the release (FEATURE-1795), which
  describes the whole search.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing made factually wrong. The README's "a search that highlights what it found" is still true.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the new and the updated tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
