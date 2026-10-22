# FEATURE-3D77 — History ages follow the auto refresh

**Item:** FEATURE-3D77 — History ages follow the auto refresh
**Branch:** `feature/feature-3d77-history-ages`
**Run:** vibe/2026-10-22-history-ages-win-publish

## Summary

When the history shows *How long ago*, each line's age is now measured again on every automatic
refresh. A line that said "just now" says "2 minutes ago" two minutes later, without pressing
*Refresh everything*.

- **The row.** `CommitRowViewModel.RelativeDate` is now observable: a `field`-backed property with a
  private setter. When it changes, it also raises `DateText` and `DateTooltip`.
  `RefreshRelativeDate(now)` measures the commit's age again. The uncommitted line has no age and is
  left alone.
- **The page.** `HistoryPageViewModel.RefreshRelativeDates(now)` measures every row again.
  `RefreshInPlaceAsync` calls it first, with `DateTimeOffset.Now`. Every periodic tick reaches
  `RefreshInPlaceAsync` (through `MainWindowViewModel`'s `Refreshed` handler), and so do the
  requested refresh and the toolbar's fetch, pull and push.
- **Nothing is redrawn.** The rows are the same instances, so the selection and the scroll stay
  where they are. Only rows whose phrase changed notify. A commit older than a day costs one string
  format per tick.
- **Absolute mode.** The relative age lives in the tooltip, and it moves the same way.

The ages follow the automatic refresh's interval, which is the *Automatic refresh* setting (60 s by
default, 15 s for you). With the automatic refresh off, they move only on a redraw, as before.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`:
  - `RelativeDate` is observable;
  - `RefreshRelativeDate(DateTimeOffset)`;
  - the class remark names the exception to "formatted once".
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`:
  - `RefreshRelativeDates(DateTimeOffset)`;
  - `RefreshInPlaceAsync` calls it, and its remarks say so.
- `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`: a new test,
  `TheHistory_MeasuresItsAgesAgainWhenNothingMoved`. The rows are aged by three hours, then a
  refresh in which nothing moved brings them back to "just now", with the same row instances.
- `docs/roadmap.md`, `docs/plan/FEATURE-3D77.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryAgeTests.cs`, four tests:
  - a later age is shown and notified;
  - absolute mode moves only the tooltip;
  - the same age notifies nothing;
  - the uncommitted line has no age.
- `docs/done/FEATURE-3D77.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where in `RefreshInPlaceAsync` | Right after the "no repository / reload running" guard, before the stash and status reads | Every later path is covered, including "moved but the diffs hold the page", where the rows stay up while the redraw waits. A running reload draws fresh rows anyway |
| Which notifications | `DateText` and `DateTooltip` both, whatever the mode | One of them shows the age in each mode. Raising the other re-reads an unchanged string, which is simpler than branching on the mode |
| `RelativeDate`'s initial value | `= string.Empty` initialiser | Satisfies nullable analysis for the `field`-backed property. The uncommitted line's explicit `string.Empty` then notifies nothing |
| Test placement | Row behaviour in a new `HistoryAgeTests.cs`; page behaviour beside `TheHistory_StaysExactlyAsItWasWhenNothingMoved` in `AutoRefreshTests.cs` | Matches the existing split: row-level files like `HistorySearchTests`, and the auto refresh's "what the graph does with it" section |

## Deviations & follow-ups

- No deviation from the plan.
- **The Desktop suite is not run** on this machine (standing instruction). The five new tests are
  written and compiled. Run the Desktop suite on Linux before tagging.
- **Follow-up, if wanted:** other relative dates still freeze at the moment they were drawn: the
  commit details dialog, and the branches and tags pages. They are out of this item's scope, which
  named the history.
- Line endings: the touched files are LF, as `.gitattributes` asks; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: 388 total, 386 passed, 0 failed, 2 skipped (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction). Five tests added.

## Documentation sweep

Nothing stale. The README and `RELEASENOTES.md` do not say how often the history's ages are measured.
The README's "every 60 seconds by default" refresh remains true.
