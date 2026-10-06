# FEATURE-3E91-PHASE01 — Search SHAs and authors

**Item:** FEATURE-3E91 — Search SHAs, authors, step through (PHASE01 of 2)
**Branch:** `feature/feature-3e91-phase01-sha-author-search`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

The history's search box now finds a commit by its SHA and by its author, as well as by its message.

`CommitRowViewModel.Matches(search)` is true when any of these holds:

- the subject or the body contains the search, case-insensitively (unchanged);
- the commit's full SHA **starts with** it, case-insensitively;
- the author's name or email **contains** it, case-insensitively.

The uncommitted line still matches nothing. The box's placeholder reads "Search messages, SHAs and
authors", and its accessible name says the same. The marking, the count and the summary are
unchanged: they run on the row's `Matches`.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`: `Matches`.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: doc comments only
  (`SearchText`, `MarkMatches`).
- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the placeholder and the accessible
  name.
- `docs/roadmap.md`, `docs/plan/FEATURE-3E91.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistorySearchTests.cs`:
  - a SHA by its start, in any case, short or full;
  - not by its middle or its end;
  - a hex word inside a SHA marks nothing;
  - an author by name or email, anywhere and in any case;
  - another author is not found;
  - the message is still searched;
  - an empty search finds nothing;
  - the uncommitted line matches nothing.
- `docs/done/FEATURE-3E91-PHASE01.md`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Comparison for the SHA | `OrdinalIgnoreCase` | A SHA is hex, not language: culture rules have nothing to say about it |
| Comparison for names and emails | `CurrentCultureIgnoreCase`, like the message | Names are text a person typed, and the box already searched messages that way |
| Accessible name | "Search the commits by message, SHA or author" | It says what the box does to someone who can't see the placeholder |

## Deviations & follow-ups

- **Not run:** the new tests are compiled but were not run, because you asked to skip the Desktop
  tests this session. The existing `HistoryPageTests` search cases (`branch`, `support-4213`,
  `readme`, `uncommitted`, `nothing matches this`) were checked by reading: none of those words is a
  SHA prefix or part of the test author's name or email, so their expected counts still hold.
- README: the *Features* line ("a search that highlights what it found…") is still true. FEATURE-1795
  will say what the search finds.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing made factually wrong. See above.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the new tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
