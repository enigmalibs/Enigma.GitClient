# FEATURE-3D77 — History ages follow the auto refresh

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-3d77-history-ages`
**Run:** vibe/2026-10-22-history-ages-win-publish

## Objective

When the history shows dates as *How long ago*, each age ("just now", "5 minutes ago") is measured
once, when the rows are drawn. Only *Refresh everything*, which redraws the rows, measures them again.

The periodic automatic refresh must also bring the ages up to date, so a line that said "just now"
says "2 minutes ago" two minutes later.

## Context & constraints

- `CommitRowViewModel` formats `RelativeDate` in its constructor from the `now` that
  `HistoryPageViewModel.AppendPage` passes (`DateTimeOffset.Now`). The property is get-only.
  `DateText` / `DateTooltip` pick the relative or the absolute date by `ShowsAbsoluteDate`.
- Every periodic tick of `AutoRefreshService` publishes `Refreshed` with `Changes = Everything`.
  `MainWindowViewModel` passes it to `HistoryPageViewModel.RefreshInPlaceAsync`.
  `RefreshInPlaceAsync` returns without touching the rows when nothing moved. This is deliberate:
  `AutoRefreshTests.TheHistory_StaysExactlyAsItWasWhenNothingMoved` checks that the row instances
  stay the same.
- The tick's interval is the `AutoRefreshSeconds` setting. The default is 60 s; the user runs 15 s.
  The ages follow whatever interval is set, and do not move when the automatic refresh is off.
- House MVVM: no source generators. Observable properties use the `field` keyword and `SetProperty`.
- The Desktop test suite is written and compiled but **not run** on this machine (standing
  instruction). The Core suites are run.

## Steps

1. `CommitRowViewModel`:
   - `RelativeDate` becomes a `field`-backed property with a private setter. When the value changes,
     it raises `RelativeDate`, `DateText` and `DateTooltip`.
   - A public `RefreshRelativeDate(DateTimeOffset now)` measures the commit's age again. It does
     nothing on the uncommitted-changes row.
   - The class remark ("formatting happens once") says the age is the exception.
2. `HistoryPageViewModel`:
   - A public `RefreshRelativeDates(DateTimeOffset now)` calls the row method on every row.
   - `RefreshInPlaceAsync` calls it with `DateTimeOffset.Now` once it knows there is a repository and
     no reload is running, before deciding whether to redraw. Its doc says so.
3. Tests (Desktop suite, compiled), in a new `HistoryAgeTests.cs`:
   - a row measured again later says the later age and raises `RelativeDate` and `DateText`;
   - in absolute mode, `DateText` is unchanged and `DateTooltip` carries the new age;
   - measuring again at the same age raises nothing;
   - the uncommitted row stays empty;
   - `RefreshInPlaceAsync` with nothing moved brings aged rows back to their current age and keeps
     the same row instances.

## Acceptance criteria

- After a periodic automatic refresh in which nothing moved, every commit line's relative date is
  measured from the time of that refresh. The rows are not redrawn: same instances, same selection,
  same scroll.
- In absolute mode, the relative age in the tooltip is brought up to date the same way.
- A refresh that redraws still formats fresh rows, as before.
- The build is clean with zero warnings. The Core suites are green. The Desktop suite compiles.

## Out of scope

- Changing the automatic refresh's interval or its default.
- A separate timer for the ages when the automatic refresh is off.
- Relative dates elsewhere: the commit details dialog, branches, tags and other pages.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the ages are measured again | At the start of `HistoryPageViewModel.RefreshInPlaceAsync` | Every periodic tick reaches it, and so do the requested refresh and the toolbar's sync. The page owns its rows. The "nothing moved → same instances" contract is kept | A second call in `MainWindowViewModel`'s handler (splits the logic); only on the "nothing moved" path (aged rows would stay stale while the diffs hold the page) |
| What triggers it | The automatic refresh, as asked | It is what the draft names | A dedicated `DispatcherTimer` (new machinery the draft did not ask for) |
| Row update | `RefreshRelativeDate(now)`; `RelativeDate` observable through `SetProperty` | House MVVM convention. Only rows whose text changed notify, so most rows (days old) cost one string format | Rebuilding the rows (loses place, selection, scroll) |
| Absolute-date mode | Still measured again | The tooltip shows the relative age in that mode | Skipping it |
| Clock | `DateTimeOffset.Now`, as `AppendPage` uses; the page method takes `now` for tests | Same clock as the rows were drawn with | Injecting `TimeProvider` (would move every row onto the test container's manual clock, which cannot be checked while the Desktop suite cannot run here) |
| Type | FEATURE | The draft asks to add a refresh | BUG |
