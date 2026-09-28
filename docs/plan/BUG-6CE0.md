# BUG-6CE0 — History badges arrive late

**Status:** DONE
**Type:** BUG
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

Opening some repositories draws the history's lines and graph at once, but the branch and tag badges
(the Refs column) only appear much later — or at once if the reader presses Refresh. The badges must
be there as soon as git has named them, and the page must show a loader at its top while it is still
reading anything.

## Context & constraints

- **Root cause.** `RepositoryContext.OpenAsync` empties `Refs`/`Decorations`/`Head`, raises
  `RepositoryChanged`, and only then awaits `RefreshAsync` (`for-each-ref` + `symbolic-ref` +
  `rev-parse`). `HistoryPageViewModel.OnRepositoryChanged` starts `ReloadAsync()` straight away, so
  the log read and the reference read race.
  - `AppendPage` takes the badges from `RepositoryContext.Decorations` *as they are when the page
    lands*. When the log wins — a repository with many branches, where `%(upstream:track)` makes
    `for-each-ref` slow — the rows are drawn with no decorations, and `_drawnStamp` records the empty
    state.
  - `OnRepositoryStateRefreshed` does not redraw. The next automatic refresh (15 s by default) sees
    `_drawnStamp` differ and redraws; the Refresh button forces it. That is the reported delay.
  - The walk's `--exclude` list (hidden branches) is also read from `RepositoryContext.Refs` when the
    page is asked for: a read that started before the references were known walks hidden branches too.
- BUG-6B9E deliberately kept "redraw from `StateRefreshed` on every re-read" out of scope: operations
  already reload after their own re-read. The fix therefore reacts to the **first** state after an
  opening only.
- Page busy state: `IsBusy` is set by `LoadPageAsync` only.

## PHASE01 — Badges as soon as the references arrive

**Branch:** `bugfix/bug-6ce0-phase01-badges-on-arrival`
**Status:** DONE — see `docs/done/BUG-6CE0-PHASE01.md`

### Steps

1. `HistoryPageViewModel`:
   - remember whether the first page was **walked** before the context had a state
     (`RepositoryContext.Head is null` when the query is built) and whether it was **drawn** before
     it (`Head is null` in `AppendPage` for the first page);
   - in `OnRepositoryStateRefreshed`, once per opening: if the rows were drawn without a state, redraw
     them keeping the reader's place (`ReloadKeepingPlaceAsync`); if a read is still in flight that was
     walked without a state and hidden branches now apply, restart it (`ReloadAsync`); otherwise the
     read in flight picks the decorations up when it lands.
2. Tests (headless, real git) with a gated reference reader so the order is forced:
   - rows drawn before the references → the badges appear as soon as the state arrives, without a
     refresh tick;
   - references before the rows → a single read, badges present;
   - a hidden branch and a read in flight before the state → the hidden branch's commits are not in
     the graph once it settles.

### Acceptance criteria

- On opening, the badges appear as soon as the references are read, whichever read finishes first.
- No redraw is added to an ordinary refresh or operation (the first state of an opening only).
- Build clean with zero warnings; the whole suite green.

## PHASE02 — A loader at the top of the history

**Branch:** `bugfix/bug-6ce0-phase02-history-loader`
**Status:** DONE — see `docs/done/BUG-6CE0-PHASE02.md`

### Steps

1. `HistoryPageViewModel.IsLoading`: `IsBusy`, or a repository is open and the context has not read
   its state yet (`Head is null`); notified from `OnBusyChanged`, `OnRepositoryChanged` and
   `OnRepositoryStateRefreshed`.
2. `HistoryPageView.axaml`: a thin indeterminate `ProgressBar` docked under the toolbar, visible
   while `IsLoading`, named for accessibility; it takes no height away from the rows when hidden.
3. Tests: the flag follows the first read and the reference read; the bar is visible while loading
   and collapsed afterwards.

### Acceptance criteria

- While the history reads its commits, and after an opening until the references are read, a loader
  is visible at the top of the view; it disappears once everything is drawn.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Making `for-each-ref` itself faster (e.g. dropping `%(upstream:track)` from the first read).
- Redrawing the history on every re-read of the context.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to fix the race | React to the first state of an opening: redraw in place when the rows were drawn without it, restart a stale in-flight walk when hidden branches apply | Keeps the two reads in parallel (fast repos stay fast) and touches nothing else; exactly what the Refresh button does, which the reader already sees working | Waiting for the references before reading the log (slower for every repository); redrawing on every `StateRefreshed` (double reads after each operation — BUG-6B9E's out-of-scope) ; re-decorating rows in place (wrong when hidden branches change the walk) |
| What "loading" covers | The history's own read, plus an opened repository whose references are not read yet | "While it loads everything" — the badges are part of it | The log read only |
| Loader form | Thin indeterminate progress bar under the toolbar | The usual top-of-view loader; no layout jump | An overlay spinner over the rows (hides what is already there) |
| Two phases | Fix, then loader | One reviewable commit each | One dev |
