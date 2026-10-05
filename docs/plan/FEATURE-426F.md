# FEATURE-426F — A fetch when a repository opens

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-426f-fetch-on-open`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

Opening a repository fetches from its remotes. And check that the automatic refresh (every 15 s) and the
toolbar's refresh button both fetch.

## Context & constraints

- **Checked: both already fetch.** `Services/AutoRefreshService.RefreshAsync` (the periodic tick, and
  `RequestRefreshAsync` behind the refresh button) calls `ISyncOperations.FetchQuietlyAsync`, which runs
  `git fetch --progress --prune --tags --all` under the non-blocking write lock, then re-reads the
  state. Existing tests pin it: `AutoRefreshTests.ItTicks_AtTheIntervalTheSettingsName`,
  `ARequestedRefresh_RunsTheSameFetchAndSaysItWasAskedFor`,
  `TheRefreshButton_FetchesAndRedrawsTheHistoryEvenWhenNothingMoved`. A quiet fetch that fails
  (offline, no credentials) is logged at debug level and reported to nobody.
- Nothing fetches on open: the first quiet fetch waits a whole interval, and none runs when the interval
  is 0.
- `Services/RepositoryOpener.OpenAsync` is how the start window's list, its folder picker and the command
  line open a repository (discovery → context → the profile's list). Create and clone use the context
  directly.
- `AutoRefreshService.RefreshNowAsync` runs one quiet refresh and publishes `Refreshed`, which the
  repository window turns into a history redraw when something moved.

## Steps

1. `RepositoryOpener` takes `IAutoRefreshService`; once the repository is open (its state read) it starts
   `RefreshNowAsync()` without waiting for it — whatever the interval setting.
2. Tests: opening through the opener runs one quiet fetch, also with the automatic refresh off; the opener
   returns before the fetch finishes; a fetch that moves a remote branch is drawn by the history.
3. The done doc records the check of the two refresh paths.

## Acceptance criteria

- Opening an existing repository fetches once, in the background, without an overlay.
- The 15 s refresh and the refresh button are confirmed to fetch (tests named in the done doc).
- Build clean, whole suite green, the new tests among them.

## Out of scope

- Fetching after create (no remote) or after clone (just fetched).
- Reporting a failed quiet fetch (a follow-up suggestion).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Visible or quiet | Quiet, as the periodic fetch | The window is usable at once; an offline open is not an error | The toolbar's fetch with its overlay |
| Where | `RepositoryOpener`, through the auto refresh service | The one path every "open an existing repository" takes; the service already owns quiet fetches and tells the history | A constructor side effect in the service; the main window's `Opened` |
| Interval 0 | Still fetches on open | The setting is about the periodic refresh; the open fetch was asked for unconditionally | Skipping it |
| Create / clone | No fetch | No remote, or just fetched | Fetching anyway |
| The two refresh paths | Already fetch: checked, no change | The code and the tests show it | Rewriting them |
