# BUG-6CE0-PHASE01 — Badges as soon as the references arrive

**Item:** BUG-6CE0 — History badges arrive late
**Branch:** `bugfix/bug-6ce0-phase01-badges-on-arrival`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

Opening a repository starts the history's log read and the context's reference read together. In some
repositories the reference read is slow: `%(upstream:track)` counts ahead and behind for every branch.
When the log answered first, the rows were drawn with no badge, and nothing drew them again until the
next automatic refresh, about 15 s later. The Refresh button forced that redraw, which is what the reader
saw working.

The history now catches up with the **first** reference state of an opening
(`HistoryPageViewModel.CatchUpWithTheFirstState`, called from `OnRepositoryStateRefreshed`):

- **Rows drawn before the state**, and no read running: they are redrawn in place
  (`ReloadKeepingPlaceAsync`), keeping the selection and the scroll. This is the Refresh button's path.
  While the diffs have the page, the redraw waits for them to close, as the automatic refresh does.
- **A read still running**: nothing to do. Its rows take their badges from the context when they land.
  There are two exceptions, and both restart the read (`ReloadAsync`):
  - it was walked before the state *and* some branches are hidden, so its `--exclude` list was empty;
  - rows were already drawn without a state.
- "Before the state" is `RepositoryContext.Head is null`, which is only ever true between an opening and
  its first successful reference read. Every later re-read therefore leaves the history alone, as
  BUG-6B9E intended: operations and refreshes redraw it themselves.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.App.UnitTests/HistoryOpeningTests.cs` — 4 tests over real git. A gated
  reference reader and a counting, holdable commit log reader force each ordering.

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `_drawnWithoutState` and
  `_walkedWithoutState` are set by `AppendPage` / `LoadPageAsync` and reset by `ReloadAsync`, plus
  `CatchUpWithTheFirstState`.
- `docs/roadmap.md`, `docs/plan/BUG-6CE0.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How "no state yet" is recognised | `RepositoryContext.Head is null` | The context sets it to `null` on opening, and every successful read sets a `HeadState` (an unborn one included). No new context API is needed. |
| Where the walk is marked | Just before `GetPageAsync`, after the working-tree probe | That is when `ExcludedRefs()` is read into the query. The probe can outlast the reference read. |
| Rows drawn without state while another read runs | Restart the read | The rows already drawn would keep their empty badges otherwise. This only happens right after an opening. |

## Deviations & follow-ups

- None from the plan.
- Follow-up (not done): the reference read could skip `%(upstream:track)` on its first pass to make the
  badges appear even sooner. That is out of scope here.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

Nothing user-facing became inaccurate: the README describes badges, not when they arrive. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2363 passed**, 0 failed (baseline 2359 + 4 new),
  with no fix cycle.
- With the `CatchUpWithTheFirstState()` call disabled, two of the new tests fail (rows drawn before the
  references; a walk before the references with a hidden branch), so they pin the bug.
