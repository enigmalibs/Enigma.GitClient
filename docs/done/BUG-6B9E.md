# BUG-6B9E — Graph stays stale after a push

**Item:** BUG-6B9E — Graph stays stale after a push
**Branch:** `bugfix/bug-6b9e-graph-follows-refs`
**Run:** bugfix/2026-09-27-remote-refresh-release

## Summary

The history now follows the remote branches and tags on its own. After the toolbar's push, the remote
branch's badge moves to the pushed commit at once. Anything else that moved a reference between two
automatic refreshes is redrawn by the next one, every 15 seconds by default.

**Why it stayed where it was.** The graph was redrawn only when an automatic refresh saw a reference
move *during its own fetch*: `AutoRefreshService` stamps the context before and after, and the window
passes `Changed` on.

- The toolbar's push re-reads the references as it ends (`RunExclusiveAsync(refreshAfter: true)`), so
  `origin/<branch>` had already moved in the context, but nothing told the history.
- Every later tick therefore started from the moved state, found nothing new, and left the rows with
  the badges they were built with.
- Only the refresh button, which redraws whatever it finds, got past it.

The toolbar's pull and fetch had the same gap, as did any partial commit on the Changes page.

**The fix.**

- **`HistoryPageViewModel` remembers the stamp its rows were drawn from** (`_drawnStamp`).
  - `AppendPage` takes it for the first page, at the moment it takes the badges from the context. So
    it is exactly what is on screen, even when a repository's first read began before the context had
    read the references.
  - `ReloadAsync` takes it first. When that read fails, the history is not read again (and the error
    reported again) at every tick.
- **`RefreshInPlaceAsync` redraws when the context's stamp differs from it**, whatever the refresh saw
  on its own. Its argument still forces a redraw, which is what the refresh button uses. Nothing new
  still means nothing is redrawn, and a redraw still keeps the selection and the scroll offset, and
  waits for the diffs to close.
- **The toolbar's Fetch, Pull and Push** (`MainWindowViewModel.RunSyncAsync`) call
  `RefreshInPlaceAsync(false)` once they return. The graph is current as soon as the operation ends,
  and is left alone when nothing moved. The history's own context-menu push already did this.

## Files / modules touched

**Created**

- `docs/done/BUG-6B9E.md`

**Modified**

- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — `_drawnStamp`, taken in
  `ReloadAsync` and `AppendPage`; `RefreshInPlaceAsync` compares with it; documentation.
- `src/Enigma.GitClient.App/ViewModels/MainWindowViewModel.cs` — keeps the history; `RunSyncAsync`
  brings it up to date after the operation.
- `tests/Enigma.GitClient.App.UnitTests/AutoRefreshTests.cs` — four tests, a bare-remote helper, a
  working-directory `Git` overload, and a push hook on the scripted sync.
- `docs/roadmap.md`, `docs/plan/BUG-6B9E.md` — statuses.

## Tests

New, in *what moved between two refreshes*. Each drives a real git against a real bare remote,
`origin`, that is one commit behind `main`:

| Test | What it proves |
|---|---|
| `TheHistory_RedrawsARemoteBranchAPushMovedSinceItWasDrawn` | A real `git push`, then the context's re-read: `RefreshInPlaceAsync(false)` redraws once, `origin/main`'s badge moves from "Add the readme" to "Pushed later", and a second refresh redraws nothing |
| `TheHistory_RedrawsATagFetchedSinceItWasDrawn` | A tag made on the remote and brought by `git fetch --tags` gets its badge |
| `TheNextRefresh_RedrawsAPushMadeBetweenTwoRefreshes` | Through the window: a push made between ticks is redrawn by the next tick, although that tick's `Changed` is `false` — the exact bug |
| `TheToolbarPush_RedrawsTheHistoryAtOnce` | The toolbar's Push command leaves the badge on the pushed commit and the selection where it was, with no automatic refresh involved |

**All four fail against the old code.** With the stamp comparison and the toolbar redraw taken out, the
four new tests fail and the other 19 in the class pass. The fix was then restored as it is.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| When the drawn stamp is taken | In `ReloadAsync`, then again in `AppendPage` for the first page | `AppendPage` is where the badges are read, so its stamp is what is drawn. Taken only at the start of the read, it would have been the empty state on every repository opening (the reload starts before the context's first read), and the first tick after every opening would have redrawn for nothing. `ReloadAsync`'s own stamp is kept when the read fails, so a failing history is not re-read and re-reported every 15 seconds |
| A test's push | A real `git push` to a bare remote in the test's folder | The remote-tracking branch moves exactly as the app's push moves it; no network, no credentials |
| The toolbar test's push | The scripted sync's new `OnPush` hook, pushing for real and re-reading the context | That is what `SyncOperations.PushAsync` amounts to once the guard and the overlay are left out; the test is about what the window does next |
| `RefreshInPlaceAsync`'s argument | Kept, now documented as a force | The refresh button's `Requested` relies on it; no caller changes |

## Deviations & follow-ups

- **Deviation from the plan:** the plan took the stamp in `ReloadAsync` only. `AppendPage` takes it for
  the first page too, for the reason in the first decision above.
- **A one-off failure, not reproduced.** The first full-suite run of this dev reported one failed test
  out of 2359, and its name was not captured. The next 15 full runs were all green (2359/2359), and the
  `AutoRefreshTests` class, run alone 25 times, never failed. No fix was attempted, so no fix cycle was
  spent. Earlier devs have recorded timing flakes in the integration tests (`BUG-39D9`,
  `FEATURE-14E8-PHASE02`). If it recurs, capture its name with
  `dotnet test ... > log; grep '^failed '`.
- Out of scope, as planned: a tag deleted on the remote stays local (`--prune` does not prune tags
  that `--tags` fetched), and a tag moved on the remote is not updated without `--force`.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

Nothing the diff made wrong:

- the README and `docs/RELEASE.md` say nothing about the automatic refresh;
- `RELEASENOTES.md`'s mentions are historical (1.1.0);
- the release item `FEATURE-2408` adds the fix to the 3.0.0 notes.

No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2359 passed**, 0 failed, 0 skipped (the
  2355 of the baseline plus the four new tests), in 15 consecutive full runs, after the one unexplained
  failure above.
- `AutoRefreshTests` alone: 23/23, 25 times in a row.
