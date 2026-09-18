# FEATURE-14E8-PHASE02 — Branch badges in their own column

**Item:** FEATURE-14E8 — History: diffs, badges and dragging
**Branch:** `feature/feature-14e8-phase02-badge-column`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

The ref badges now stand in a column of their own, between the graph and the subject, and that
column is the same width on every line — so the subject, the author, the date and the short hash
start at the same x whether or not the row carries a badge.

The column was already there in the row's `Grid`; what it was not was a *column*. Its
`ColumnDefinition` is `Auto`, and every row is its own `Grid`, so `Auto` measured per row: a line
decorated with `main` pushed its own subject 60 px to the right while the line under it did not
move. The graph beside it had solved this long ago — `GraphColumnWidth` is one number for the whole
page, handed to each row's `CommitGraphCell` as an explicit `Width` — and this phase gives the badge
strip the same treatment through a new `RefColumnWidth`.

Deciding that one width means knowing how wide a badge is, which is its label in the badge's own
face plus the chrome the template draws round it. `Controls/RefBadgeMetrics.cs` holds that
arithmetic: the padding, the icon, the icon gap and the spacing between badges as constants
mirroring `Themes/Controls.axaml`, and the label measured through `FormattedText` and cached per
string — with the same fallback `DiffTypography` uses, so a ViewModel built before there is a font
manager gets an estimate instead of an exception. The page takes the widest strip across its loaded
rows and clamps it to `MaximumRefColumnWidth` (280), because one 200-character branch name is not a
reason to take the subject's room; past that the badge's own `MaximumTextWidth` ellipsises it.

The strip lost its `IsVisible="{Binding HasRefs}"`. That binding was the other half of the
misalignment: an invisible child desires nothing, so an undecorated row's column collapsed to zero
while its neighbour's did not. An empty `ItemsControl` of the page's width draws nothing and holds
the column, which is exactly what is wanted. When no row is decorated at all the width is 0, so an
undecorated history still spends nothing on it.

## Files / modules touched

**Created — App**

- `Controls/RefBadgeMetrics.cs` — how wide a badge and a row's strip of them are, with the template
  constants it mirrors and a cached, fallback-guarded label measurement

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — `RefColumnWidth` and `MaximumRefColumnWidth`,
  recomputed as each page is appended and cleared with the rows on reload
- `Views/Pages/HistoryPageView.axaml` — the badge strip is named `RefStrip`, takes the page's width,
  clips to it, and no longer hides itself on an undecorated row

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — three tests: the column is empty when
  nothing is decorated, it grows with a longer branch name and stops at its maximum, and every
  shown row's strip is the page's width with every subject at the same x

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-14E8.md`, `RELEASENOTES.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `IsVisible` on the strip | Removed | It is the other half of the same defect: an invisible child desires nothing, so an undecorated row's column collapsed while a decorated one's did not |
| Where the template's measurements live | Constants in `RefBadgeMetrics`, mirroring the theme | A per-badge binding to a theme resource would walk the resource system for thousands of rows to learn one number that changes only when the template does |
| Which typeface is measured | `Typeface.Default` at the badge's 11 px | The badge's label inherits the application's face; the theme sets no family of its own for it |
| Clamping | `Math.Min` against a page-level maximum, with the badge's own ellipsis past it | One absurd branch name must not cost every row its subject |
| The alignment assertion's tolerance | Within one pixel | Layout rounding snaps a measured width onto the device grid; the measurement is in points, so exact equality is the wrong question |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- **Follow-up — an unrelated flaky test.** `Enigma.GitClient.Core.IntegrationTests`'
  `SyncServiceTests.FetchAsync_ReportsItsProgress` failed once in six full-suite runs of this dev
  (`Assert.NotEmpty` on the progress reports) and passed in the other five. It dates from
  `FEATURE-06FE-PHASE01`, drives a real `git fetch` over a local clone, and asserts that git
  narrated the transfer; `Progress<T>` delivers its callbacks on the thread pool, so a report can
  land after the fetch has returned. Nothing in this run touches sync. Recommended fix when it is
  next worth a dev: wait for the first report rather than assert immediately after the await.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1644  failed: 0  succeeded: 1644  skipped: 0
```

Run six times in total while chasing the flake above: five clean, one with the unrelated sync
failure. Three tests are new. One fix cycle was needed, for the layout-rounding tolerance.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`.

`RELEASENOTES.md`'s graph section was made untrue by **PHASE01** and its sweep missed it — two
bullets still said that selecting a line opened the diffs and that double-clicking checked a branch
out. Both are corrected here: a double-click or the row's menu opens what a line changed, and
checking out is the menu's job. Nothing in either file describes the badges' layout, so this phase's
own change made nothing else stale. There is no `CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in
the repository.
