# FEATURE-92A3-PHASE01 — No branch-scope selector

**Item:** FEATURE-92A3 — History: no scope, no first parent
**Branch:** `feature/feature-92a3-phase01-no-scope-selector`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

The History toolbar no longer has its "All branches / Current branch" combobox. The graph always walks
every branch, tag and remote-tracking ref, plus HEAD — what "All branches" did — which is the commit-log
query's own default scope, so nothing sets it any more.

Gone with it: the `HistoryScopeOption` record, `ScopeOptions`, `SelectedScope`, and the constructor
line that selected the first option. That line also triggered a read of the history as the page was
built; the page already reads its history when it first appears (`OnAppearingAsync` with no rows), so
that read was a second one done before anything was on screen, and it is gone too.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml` — the combobox removed, the toolbar grid renumbered
- `ViewModels/Pages/HistoryPageViewModel.cs` — `HistoryScopeOption`, `ScopeOptions`, `SelectedScope`
  removed; a remark saying the graph is always the whole repository

**Tests**

- `App.UnitTests/HistoryPageTests.cs`
  - `Page_ScopeSelectorSwitchesBetweenAllBranchesAndTheCurrentOne` → `Page_ShowsEveryBranch_NotOnlyTheCurrentOne`
    (a branch that is not checked out is in the graph)
  - `Toolbar_HasNoBranchScopeSelector` (new) — the realised page has no combobox
  - `Page_HidesMergedInBranchesWithFirstParentOnly` no longer narrows to the current branch: it deletes
    the merged topic branch, so the topic commit is reachable only through the merge and first-parent
    history is what leaves it out (the test goes with the checkbox in PHASE02)
  - `OpenOverProbeAsync` no longer answers a read made while the page is built — there is none now,
    which the helper asserts

## Deviations & follow-ups

- The engine's `CommitLogScope` stays, as planned: Core and its integration tests use `Head` and
  `Revision`.
- The dropped construction-time read was found by the two overlapping-load tests, whose helper
  answered it (fix cycle 1 of 3). The first read on screen is unchanged: `Page_ClearsItselfWhenTheRepositoryIsClosed`
  covers the page reading its history when it appears.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2152 passed, 0 failed (1 new; 1 replaced).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Keep a read at construction to replace the one `SelectedScope` made | No | The page reads when it appears; a read before that is work for a page nobody sees yet |
| How the first-parent test survives until PHASE02 | Delete the merged topic branch in that test | With every ref walked, the topic's own ref would otherwise keep its commit in the graph |
