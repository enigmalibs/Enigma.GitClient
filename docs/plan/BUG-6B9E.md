# BUG-6B9E — Graph stays stale after a push

**Status:** DONE — see `docs/done/BUG-6B9E.md`
**Type:** BUG
**Branch:** `bugfix/bug-6b9e-graph-follows-refs`
**Run:** bugfix/2026-09-27-remote-refresh-release

## Objective

Pushing the current branch from the toolbar works, but the history keeps drawing the remote branch
where it was before the push. The automatic refresh never moves it, however long you wait; only the
toolbar's refresh button does. The history must follow the remote branches and tags on its own: at
once after the toolbar's own fetch, pull or push, and otherwise at the next automatic refresh (every
15 seconds by default).

## Context & constraints

- **Root cause.** The automatic refresh redraws the graph only when its own tick saw a reference
  move: `AutoRefreshService` stamps the context before and after the quiet fetch
  (`RepositoryStateStamp`), and `MainWindowViewModel` calls
  `history.RefreshInPlaceAsync(result.Changed || result.Requested)`.
  - A toolbar **Push** — and **Pull** and **Fetch** — runs through
    `IRepositoryContext.RunExclusiveAsync(refreshAfter: true)`. That re-reads the references: after a
    push, `refs/remotes/origin/<branch>` has moved in the context. But nothing redraws the history
    (`MainWindowViewModel.RunSyncAsync` only notifies the tracking strip).
  - Every later tick stamps the context *after* that move, so its `Changed` is always `false`. The
    rows keep the badges `AppendPage` built from the decorations of the time.
  - Only **Refresh** (`Requested`) forces a redraw.
- The same gap hides any move made between ticks by the app itself: a partial commit on the Changes
  page, for example. Moves made outside the app (a terminal) are caught, because the context has not
  re-read them yet when the tick stamps it.
- The history's own context-menu push already redraws at once (`OnPushBranchAsync` →
  `ReloadAsync`). Secondary dialogs redraw on close when the stamp moved (`OpenToolAsync`).
- The quiet fetch is `FetchAsync(handle, null, prune: true, fetchTags: true, …)`: new, moved and
  pruned remote branches and new tags arrive as local references, all of which
  `RepositoryStateStamp` covers (`RefCollection.All()`).
- The interval is the existing setting `AppSettings.AutoRefreshSeconds`, default **15**, range 5–3600.
- **Baseline:** clean Release build, 2355 tests green.

## Decisions

| Decision | Rationale |
|---|---|
| `HistoryPageViewModel` remembers the stamp its rows were drawn from (taken when `ReloadAsync` reads the first page) and `RefreshInPlaceAsync` redraws when the current stamp differs from it, whatever its argument | The graph compares against what it shows, not against the tick's start, so every path that moved a reference between ticks is caught |
| `RefreshInPlaceAsync(bool)` keeps its signature; the argument still forces a redraw | `Requested` (the refresh button) keeps redrawing even when nothing moved; no caller changes |
| The toolbar's Fetch, Pull and Push call `RefreshInPlaceAsync(false)` once they return | They redraw at once, keeping the reader's place, and only when something moved, as the context-menu push already does |

## Steps

1. `HistoryPageViewModel`:
   - a `_drawnStamp` field, set in `ReloadAsync` just before the first page is read;
   - `RefreshInPlaceAsync` redraws when `referencesMoved` is set, when the current stamp differs from
     `_drawnStamp`, or when the uncommitted row is out of date, as today;
   - its documentation updated.
2. `MainWindowViewModel`: keep the history in a field; `RunSyncAsync` awaits
   `_history.RefreshInPlaceAsync(referencesMoved: false)` after the operation.
3. Tests in `AutoRefreshTests` (real git, headless):
   - a remote-tracking branch moved and re-read after the history was drawn, as a push leaves it:
     `RefreshInPlaceAsync(false)` redraws, and the badge is on the new commit;
   - a tag that arrived after the history was drawn: redrawn, badge shown;
   - after such a redraw, the next quiet refresh leaves the rows alone;
   - wired through the window: a move between two ticks is redrawn by the next tick, although that
     tick's `Changed` is `false`;
   - the toolbar's Push redraws the history at once, with the remote badge moved.

## Acceptance criteria

- After a toolbar push, the remote branch's badge is on the pushed commit without pressing Refresh.
- A reference that moved between two automatic refreshes — a remote branch, a tag, a local branch —
  is redrawn by the next one.
- A refresh in which nothing moved still leaves the history exactly as it was.
- Build clean with zero warnings; the whole suite green, including the new tests.

## Out of scope

- Changing the interval, its default or its bounds.
- Polling the remote for tags (`git ls-remote`); pruning local tags the remote deleted.
- Redrawing the history from `StateRefreshed` on every re-read of the context.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the fix goes | The history compares the current references with those it was drawn from | The root cause: it compared a tick's start with its end, which misses every move the app made between ticks | Stamping in `AutoRefreshService` across ticks (it does not know what the graph drew); redrawing on every `StateRefreshed` (double redraws after operations that already reload) |
| Redraw right after the toolbar's sync | Yes, in place, only when something moved | The context-menu push already does; the reader expects the badge to move when the push ends | Waiting up to one interval |
| "Every 15 s" | The existing setting, whose default is 15 s | Already what was asked; keeps the reader's control | Hard-coding 15 s |
| Remote tags | The same stamp: tags arrive through the quiet fetch's `--tags` | No extra network call per tick | `git ls-remote --tags` each tick |
| Phasing | One phase | One reviewable commit | Splitting the toolbar redraw out |
