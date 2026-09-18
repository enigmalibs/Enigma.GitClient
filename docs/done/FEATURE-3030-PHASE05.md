# FEATURE-3030-PHASE05 — Search highlights instead of filters

**Item:** FEATURE-3030 — History list: columns, menus, search
**Branch:** `feature/feature-3030-phase05-search-highlight`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

The search box marks the lines it finds and removes none.

It used to write `CommitLogQuery.MessageFilter` and re-read the history, which is why the graph
stopped making sense while a search was live: the lanes are laid out a page of commits at a time, so
a page filtered down to the matches draws curves between commits that are not adjacent in the
history — a picture of a repository that does not exist. Nothing is hidden now, so the lanes stay
the ones git built.

`SearchText` marks the loaded rows instead, over the subject and the body, case-insensitively —
exactly what git's `--grep` matched before, so the same words still find the same commits. The
300 ms debounce went with the query: it existed to avoid a git process per keystroke, and a
substring match over the rows in memory needs no delay.

The price is that the search reaches what is loaded rather than the whole history, which is the
honest reading of "highlight the lines that were found" — a line that is not on screen cannot be
highlighted. `Load more commits` marks the rows it brings in as they arrive, and a count beside the
box says how many were found, because with nothing hidden a search that matched nothing and one that
matched everything otherwise look identical.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — `SearchText` marks instead of filtering;
  `MatchCount`, `HasSearch` and `MatchSummary`; `MarkMatches`, called on every keystroke and on
  every page appended; `SearchDebounce`, `QueueDebouncedReload`, `DebounceAsync` and the debounce
  token source are gone
- `ViewModels/Pages/CommitRowViewModel.cs` — `IsSearchMatch`, the row's one settable observable
  property, and `Matches`, which answers for the subject and the body and never for the
  uncommitted-changes row
- `Views/Pages/HistoryPageView.axaml` — the row grid takes `Classes.match`; the match count sits
  before the search box
- `Themes/Styles.axaml` — `Grid.match`
- `Themes/Graph.axaml` — `SearchMatchColor` in both variants and `SearchMatchBrush`

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the two filter tests are replaced by
  a "the search" section: what it marks and that it hides nothing, the body as well as the subject,
  the no-match report, clearing the box, marking the page loaded afterwards, the uncommitted row
  never matching, and `Search_MarksTheRowsOnScreen`, which checks the rendered rows carry the class
  the page says they should

**Modified — docs**

- `README.md`, `RELEASENOTES.md` (see the sweep), `docs/roadmap.md`, `docs/plan/FEATURE-3030.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Whether to keep asking git | No | Every answer git gives is a different set of rows, and a different set of rows is a different graph — which is the complaint |
| The debounce | Removed | It bought one git process per burst of typing; a substring match over loaded rows costs nothing, and a delay before a highlight reads as lag |
| What is matched | Subject and body, case-insensitively | What `--grep` matched before, so a search a reader already knows keeps finding the same commits |
| Whose culture | `CurrentCultureIgnoreCase` | This is a person's search box, not a protocol: they expect their own language's casing rules |
| Telling them how much matched | A count beside the box | With nothing hidden, no match and every match look the same on screen |
| The uncommitted row | Never matches | It has no commit message; "Uncommitted changes" is a label, and matching it would highlight a row the search cannot be about |
| The highlight | A background wash on the row grid | The row already carries a graph, a badge strip and four columns; a ring around all of it reads as a second selection |
| Where the colour lives | `Graph.axaml`, per theme variant | Every other row tint in this application is defined there and follows the variant |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- `CommitLogQuery.MessageFilter` is now unused by the application. It stays in Core: it is part of
  the reader's query surface, it is tested there, and `EmptyMessage` still reads `IsFiltered`, which
  `FirstParentOnly` keeps meaningful.
- Jumping between matches (next/previous, or scrolling the first one into view) is a reasonable
  follow-up now that the list no longer moves under the reader. Not in this request.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1693  failed: 0  succeeded: 1693  skipped: 0
```

Seven tests added, two removed. Two fix cycles: the tests naming `SearchDebounce` stopped compiling
(expected — the constant went), and xUnit's analyser rejected `Assert.Single(rows.Where(...))` in
favour of the filtering overload.

## Documentation sweep

`README.md` and `RELEASENOTES.md` both claimed a branch badge could be dragged onto another in the
graph — **PHASE03 removed that gesture and its sweep missed both lines**, which this dev corrects:

- `README.md` — the drag bullet is gone (the gesture exists nowhere at this commit; FEATURE-3B62
  brings it back on the branches page and will say so there), and a bullet now describes the
  resizable column header and the highlighting search.
- `RELEASENOTES.md` — the graph section's drag sentence is gone, and four sentences cover what this
  item actually changed: the header and its resizable columns, the row-wide menu, the smaller merge
  nodes, and the search that highlights.

There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
