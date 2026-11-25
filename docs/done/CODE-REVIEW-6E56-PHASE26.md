# CODE-REVIEW-6E56-PHASE26 — [Medium] SOLID-03 History search, stash

## Summary

The third of the history's splits takes out two pieces of plain logic the page carried inline:

- `StashFolding` (new, internal, owned by the page, no DI) is how the history draws the stash.
  - It holds the stash a reload read, by the commit each entry is recorded as, and the helper commits
    (an entry's index and untracked files) found page by page.
  - It folds a page of commits: an entry becomes one line off the commit it was made on, and a helper
    is no line unless a reference points at it.
  - It also offers `HasMoved` (did the stash change since it was drawn), `ForgetHelpers` (a reload
    from the top), `EntryAt`, and `WithoutStashRef` for the badges.
- `HistorySearch` (new, internal static) is the search's logic over the loaded lines:
  - `Mark` marks and counts the matches;
  - `PositionOf` gives the selected line's place among them;
  - `Next` finds the next or previous match, wrapping around;
  - `Summarise` gives the bar's "match 3/12", "match –/12", "no match" or nothing.
- The page keeps its public search members (`SearchText`, `IsSearchBarOpen`, `MatchCount`,
  `MatchSummary`, `MatchPosition`, the clear / next / previous commands), which the view binds, and
  delegates their logic. The stash's three fields and three methods become one `_stash` field.
- The code moved verbatim. The only rewrite is `Next`: it finds the selected line by reference, as
  `IndexOf` did on these view models, which do not override equality.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/StashFolding.cs`
- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistorySearch.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/StashFoldingTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE26.md`

## Deviations & follow-ups

- **2,209 lines, not "under ~1,800".** Over PHASE24 to PHASE26 the page went from 2,882 lines to
  2,209, with the same 80 public members. These three phases took out the branch, tag, commit and
  stash commands, the search logic and the stash folding. The rest is the page's own: paging and
  layout, the details panel and diffs, selection, the uncommitted line, refresh and the repository's
  state. A further split (the paging and layout, or the details panel) is a follow-up item.
- **`HistorySearch` holds no state.** The plan called both classes "pure state and logic". The search
  text, the match count and the selection are bound by the view and raise change notifications. They
  stay on the page, and the logic behind them moved. `StashFolding` does hold its state.
- The search's existing Desktop tests (`HistorySearchTests`) and the stash's (`HistoryStashTests`) go
  through the page and were not touched.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 25 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository.** Every check passed.
  - `StashFolding` on its own:
    - with no stash a page is drawn as it is;
    - an entry with index and untracked commits becomes one line off its base, with no helper lines;
    - a helper with a tag on it is kept.
  - The real page, on the headless platform with the real container, over a repository with four
    commits and a stash with untracked files:
    - five lines are drawn, one of them the stash;
    - "apples" matches two lines ("match –/2");
    - next selects the first match down ("match 1/2"), then the second, then wraps around;
    - previous wraps the other way;
    - "kiwi" says "no match" with the arrows off;
    - closing the bar clears the search and every mark.
- **Public surface:** the page declares the same 80 public members before and after.
- **New tests (Desktop, compiled, not run)** in `StashFoldingTests` (5):
  - no stash, no change;
  - an entry is one line off its base, its helpers are no lines;
  - a helper found on one page is folded on the next, until a read from the top;
  - a helper a reference points at is kept;
  - `HasMoved`.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the page's structure.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The search's state | Stays on the page, which the view binds; the logic moves | Bound properties need the page's change notifications. Moving them would need forwarding notifications | A `HistorySearch` object raising its own notifications, rebound by the view |
| `StashFolding`'s lifetime | A field of the page, no DI | Its state is the page's drawing state, reset with its reloads; the plan says no DI | A service shared across pages |
| The line target | Recorded, not forced | Meeting it would mean moving the page's own work, which no review finding names | Splitting paging or the details panel inside this phase |
