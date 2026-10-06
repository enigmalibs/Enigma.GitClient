# FEATURE-0743 — Roomier ref badges, a HEAD icon

**Item:** FEATURE-0743 — Roomier ref badges, a HEAD icon
**Branch:** `feature/feature-0743-ref-badge-head-icon`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

- **More room in every pill.** The badges naming branches, tags and stashes in the history's Refs
  column have `Padding="9,3"` instead of `7,2`: 2 px more on each side and 1 px more above and below.
- **A check on the checked-out branch.** The checked-out branch's pill starts with a Phosphor `Check`,
  to the left of its branch icon (and of the remote's cloud, when it is grouped with its upstream).
  The pill keeps its head colour; the check says the same thing in a shape. No other pill shows it.
- **The Refs column is still measured to fit.** `RefBadgeMetrics.HorizontalPadding` is 10 (9 of
  padding plus the 1 px border). `MeasureBadge(label, withUpstream, isCurrent)` counts the check icon
  and its gap, and `Measure` passes each item's `IsCurrent`.
- **Named icons.** The template's three icons are named `CurrentIcon`, `KindIcon` and `UpstreamIcon`,
  so tests find them by name instead of by position.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Themes/Controls.axaml`: the `RefBadge` template.
- `src/Enigma.GitClient.Desktop/Controls/RefBadgeMetrics.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryPageTests.cs`:
  - the chrome constant is now 38 (it was 34);
  - the checked-out badge is measured with its check;
  - the icon-size check covers every icon;
  - two new tests: the check leads and is measured, and the padding is `9,3`.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryGroupedBadgeTests.cs`:
  - the grouped head badge draws `Check`, `GitBranch`, `CloudArrowDown`;
  - the upstream icon is found by name.
- `docs/roadmap.md`, `docs/plan/FEATURE-0743.md`: statuses.

**Created**

- `docs/done/FEATURE-0743.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the check is shown | Always in the template, visible through `IsCurrent` | The same pattern as the upstream's cloud. An invisible child adds no width or gap to the `StackPanel`, which is what the metrics assume |
| Height | Unchanged rules: the badge is about 25 px tall | Still inside the default 36 px row, which `Badge_IsRoomierAndStillFitsAHistoryRow` checks |

## Deviations & follow-ups

- **Not run:** the new and the updated tests are compiled but were not run, because you asked to skip
  the Desktop tests this session.
- At the smallest row height the setting allows (18 px), a badge was already taller than its row
  before this change; it is now 2 px taller still. The strip centres it, so it is clipped equally at
  the top and bottom. Shrinking the badges with the row would be its own item.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing made factually wrong. The README only says "ref badges".

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors. This
  compiles the tests.
- `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed. The racing
  `AtomicFileTests` test is excluded (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Core.IntegrationTests`: 351 total, 349 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Fix budget: 0 cycles used.
