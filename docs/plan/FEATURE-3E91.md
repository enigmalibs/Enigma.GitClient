# FEATURE-3E91 — Search SHAs, authors, step through

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

The history's search box:

- finds commits by their SHA and by their author's name or email, as well as by their message;
- shows what it found on the **right** of the box as `match xx/yyy`, with up/down arrow buttons that
  go to the previous/next match.

## Context & constraints

- `HistoryPageViewModel.SearchText` → `MarkMatches()` marks the **loaded** rows (`IsSearchMatch`) and
  counts them (`MatchCount`, `MatchSummary`: `no match` / `1 match` / `N matches`), shown today on the
  **left** of the box. Nothing is filtered out — the graph's lanes stay git's (documented on
  `SearchText`).
- `CommitRowViewModel.Matches(search)` checks the subject and the body, case-insensitively; the
  uncommitted row matches nothing. The row has the commit (`Commit.Sha`, the author signature).
- The box: `HistoryPageView.axaml`, toolbar grid column 3 (`MatchSummary` TextBlock, TextBox,
  clear button). Tests pin `MatchSummary` texts (`HistoryPageTests`).
- The list is a `ListBox` bound to `SelectedRow` (two-way); `AutoScrollToSelectedItem` brings a
  selected row into view.

## PHASE01 — Search SHAs and authors

**Branch:** `feature/feature-3e91-phase01-sha-author-search`
**Status:** DONE — see `docs/done/FEATURE-3E91-PHASE01.md`

### Steps

1. `CommitRowViewModel.Matches`: also true when the commit's full SHA **starts with** the search
   (case-insensitive), or the author's name or email **contains** it (case-insensitive). The
   uncommitted row still matches nothing.
2. The box's placeholder and accessible name say what it searches: messages, SHAs and authors.
3. Tests: SHA prefix (short and full, upper/lower case), a hex word inside a SHA that does not match,
   author name, author email, and the existing message cases unchanged.

### Acceptance criteria

- Typing a commit's short SHA marks that commit; typing a name or an email marks that author's commits.
- Messages still match as before.

## PHASE02 — Step through the matches

**Branch:** `feature/feature-3e91-phase02-match-navigation`
**Status:** DONE — see `docs/done/FEATURE-3E91-PHASE02.md`

### Steps

1. `HistoryPageViewModel`:
   - `NextMatchCommand` / `PreviousMatchCommand`: select the next/previous matching row after/before
     the selected one in row order, wrapping around within the loaded rows; with nothing selected,
     next goes to the first match and previous to the last. Enabled while there is a match;
   - the summary becomes `match xx/yyy`: `xx` the selected row's 1-based position among the matches,
     `–` when the selected row is not a match; `no match` when nothing matches; empty when nothing is
     searched for. It follows the selection, the search and newly loaded rows.
2. `HistoryPageView.axaml`: the summary moves to the **right** of the box, followed by the up
   (previous) and down (next) arrow buttons (`ArrowUp` / `ArrowDown`, tooltips and accessible names),
   then the clear button. They show while something is searched for.
3. In the box, Enter goes to the next match and Shift+Enter to the previous one.
4. Tests: next/previous order and wrap-around, the counter text in each state, Enter/Shift+Enter, the
   buttons' commands, icons and names in the realised view.

### Acceptance criteria

- With matches, the right of the box reads `match xx/yyy` and the arrows go to the previous/next
  match, selecting it and bringing it into view.
- No match: `no match`, arrows disabled. No search: nothing shown.

## Out of scope

- Searching beyond the loaded rows (the search marks what is loaded; *Load more commits* stays the
  reader's).
- A distinct "current match" highlight separate from the selection.
- Committer, dates, file paths.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How a SHA matches | Prefix of the full SHA, case-insensitive | That is how git abbreviates SHAs | Contains (a hex word like `add` would match ~1% of commits) |
| How an author matches | Name or email contains, case-insensitive | The message's own rule | Exact match |
| What next/previous do | Select the matching row | The selection is the list's "where I am"; it scrolls into view and the details follow, as a click does | A separate current-match highlight (a third row state, new styles) |
| Typing | Marks and counts only; selects nothing | Selecting on every keystroke would open the details panel and read files per key | Jumping to the first match as you type |
| Counter when the selection is not a match | `match –/yyy` | Honest: no current match yet; the first arrow press gives one | `match 0/yyy`; keeping `N matches` |
| No match | `no match`, arrows disabled | Keeps the existing "found nothing" signal; layout does not jump | Hiding the summary and arrows |
| Past the last loaded match | Wrap within the loaded rows | Loading more is the reader's call | Loading more pages automatically |
| Keyboard | Enter = next, Shift+Enter = previous | The convention of every find bar; cheap | No keyboard |
| Breakdown | Two phases | Matching and navigation are separable reviews | One dev |
