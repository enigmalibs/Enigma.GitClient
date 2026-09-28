# BUG-29C8 — History column titles lack a margin

**Item:** BUG-29C8 — History column titles lack a margin
**Branch:** `bugfix/bug-29c8-column-title-margins`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Summary

Every title in the History page's column header now starts the same distance after the separator
before it, and *Graph* no longer touches the page's edge.

- **Author, Date and Commit.** Their resize grips hung off their column's left edge, so each grip's
  rule sat half a pixel before the title. They now sit in the gap before the column
  (`Margin="-14,0,0,0"`: the 10 px column spacing plus half the 9 px grip). Their rule is where the
  *Graph* and *Refs* rules are, at the start of the gap, and the title is 9.5 px after it, as *Refs*
  and *Message* already were.
- **Graph.** Its title starts at the lane padding, 8 px, where the lanes it names start, instead of at
  x = 0.
- **Unchanged.** Nothing else moved: the header cells still line up with the row cells, and every
  grip still resizes its own column. A grip reports a drag *delta*, so where it sits does not matter.

## Files / modules touched

**Created**

- `docs/done/BUG-29C8.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml`: the three grips' margins with a
  comment saying why, and the *Graph* title's margin.
- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`:
  `ColumnTitles_EachStartTheSameGapAfterTheSeparatorBeforeThem`. It checks:
  - the six titles, in order;
  - *Graph* starts at the lane padding at least;
  - every other title starts at least 8 px after the nearest rule on its left, and all five gaps are
    within 1 px of each other;
  - the header still lines up with the rows.
- `docs/roadmap.md`, `docs/plan/BUG-29C8.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to find each grip's rule in the test | The grip's middle, translated into the header's coordinates | The template draws the rule centred in the grip. Reading the template's `PART_Rule` would tie the test to the style's internals |
| Proving the test catches the bug | Ran it against the old margins, one half at a time | With the old values it fails on "Graph starts at 0". With only the grips reverted, it fails on "Author starts 0.5 after its separator" |

## Deviations & follow-ups

- None. The plan's steps were implemented as written.
- Line endings: no CRLF churn.

## Documentation sweep

The README's *History list with a column header you can resize* stays true. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2491 passed**, 0 failed, 0 skipped (1 new).
- Fix budget: no fix cycle.
