# FEATURE-D7D5-PHASE02 — Badges stacked, the line on top

**Item:** FEATURE-D7D5 — Stacked ref badges in the History
**Branch:** `feature/feature-d7d5-phase02-stacked-badges`
**Run:** feature/2026-11-05-history-badges-release

## Summary

A History line now stacks its badges one under the other. A line with several badges is taller; a
line with one badge, or none, keeps the row height of the preferences. On a tall line, the rest of the
line stays in its first band, level with the first badge: the commit's node, the subject (with the
squash flag), the author, the date and the sha. The graph's lanes run unbroken through the whole line.

- `BadgeStackPanel` (new) is the badge strip's panel. It centres the first badge in a `LineHeight`
  band, stacks the others under it `Spacing` apart, and asks for the same room below the last badge as
  above the first. With one badge it asks for exactly `LineHeight`.
- `CommitGraphCell.LineHeight` (new, default 0 = today's behaviour) puts the node, and the edges into
  and out of it, in the middle of the first band. The straight lanes and the outgoing leg still run to
  the row's bottom. The node is fitted to the band.
- The row template: the `Grid`'s `Height` became `MinHeight`. `RefStrip` is top-aligned on a
  `BadgeStackPanel`. The subject, date and sha cells are wrapped in a `RowHeight` panel at the top
  (`SubjectCell` for the subject); the author cell is that height itself.
- `RefBadgeMetrics.Measure` returns the widest badge instead of the sum, so the Refs column is seeded
  with what a stacked strip needs.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/Controls/BadgeStackPanel.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryStackedBadgesTests.cs`
  - `docs/done/FEATURE-D7D5-PHASE02.md`
- Modified:
  - `src/Enigma.GitClient.Desktop/Controls/Graph/CommitGraphCell.cs`
  - `src/Enigma.GitClient.Desktop/Controls/RefBadgeMetrics.cs`
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`
  - `docs/roadmap.md`, `docs/plan/FEATURE-D7D5.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The author cell | The `StackPanel` itself gets `Height` = `RowHeight` and top alignment, with no wrapper | A horizontal `StackPanel` already centres its children vertically within its own height |
| Naming the subject's wrapper | `SubjectCell` | Tests (and any later code) find the subject through it, not through `Grid.GetColumn` on a direct child |
| Where the node is when the band is taller than the row | The middle of the row | A band taller than the row is no band (`GetNodeCentre`) |
| Checking the look | A headless render of a three-commit history, viewed, then the scratch test deleted | The layout is visual; the screenshot showed stacked badges, the node and the text in the top band, and the lane unbroken |

## Deviations & follow-ups

- Four `HistoryPageTests` read the subject as a `TextBlock` directly in the row `Grid`, or by
  `Grid.GetColumn == 2`. They now find it through the `SubjectCell` panel.
  - `RefColumn_WidensForTheBiggerBadge` now expects the wider badge rather than the sum.
- Seen once each, in full runs where other tests were failing, then green in isolation and in the
  clean full run: `AutoRefreshTests.TheRefreshButton_PicksUpACommitMadeInATerminal` and
  `RepositoriesPageTests.OpeningANewRepository_PutsItOnTop_AndAListedOneAgain_LeavesItWhereItIs`. Both
  look like fallout from tests that failed without closing their windows. They are not in this dev's
  final run.
- `HistoryPageViewModel.MaximumRefColumnWidth`'s summary still says a badge "ellipsises" past it.
  That was already untrue before this dev (badges are clipped, not ellipsised). Left as a follow-up.

## Build/test evidence

- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 3,594 total, 3,591 passed, 3 skipped, **0 failed**.
- New tests (`HistoryStackedBadgesTests`, 12):
  - the panel's height with zero, one (even when the badge is taller than the band) and three badges;
  - `TopOf`;
  - the panel's arrangement, and a hidden child skipped;
  - the node in the row's middle without a band, and in the band on a taller row;
  - real git: a three-badge line is taller and stacks `main`, `topic`, `v2` at one x with growing y,
    the first centred in the band; the one-badge and no-badge lines are exactly `RowHeight`; the
    subject and the sha are centred in the first band; the graph cell spans the line with its node in
    the band; the Refs column is the widest badge.
- Fix budget: 3 cycles, green on the third. All three fixes were in tests; no product code changed:
  1. Updated the four `HistoryPageTests` that read the subject directly. Two new tests failed.
  2. Gave the panel test's borders a badge's left alignment, and gave the text-centre check a 1 px
     tolerance for layout rounding. The `Avalonia.Layout` qualified name did not compile inside the
     `Enigma.*` namespace.
  3. Switched it to a `using`. Green.

## Documentation sweep

- README line 25 ("ref badges and virtualised scrolling") and the other badge lines stay true; no
  edit.
