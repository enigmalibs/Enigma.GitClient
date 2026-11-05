# FEATURE-D7D5-PHASE01 — Badge order: current, branches, tags

**Item:** FEATURE-D7D5 — Stacked ref badges in the History
**Branch:** `feature/feature-d7d5-phase01-badge-order`
**Run:** feature/2026-11-05-history-badges-release

## Summary

A history line now draws its badges in one fixed order:

1. the checked-out branch;
2. the local branches;
3. the remote branches;
4. anything else (the stash, a note);
5. the tags, last.

Each group is ordered by name, ignoring case. The order is applied by a new `RefBadgeOrder` to the
line's finished badge list, at the end of `CommitRowViewModel.Project`. That is after the stash badge
is added and after a remote drawn in its local branch's badge is taken out. Before this, the order was
whatever the caller passed in: Core's decoration index put the tags before the stash and other refs,
and the Desktop then put the stash badge first.

## Files/modules touched

- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/CommitRowViewModel.cs`: `RefBadgeOrder`, and its use
    in `Project`.
  - `docs/roadmap.md`, `docs/plan/FEATURE-D7D5.md`: statuses.
- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryBadgeOrderTests.cs`
  - `docs/done/FEATURE-D7D5-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Stable sort | LINQ `OrderBy(Group).ThenBy(Name, OrdinalIgnoreCase)` | Stable, so names that differ only in case keep their order; the same comparer as Core and the reference lists |
| `RefBadgeOrder.Group` public | Yes | Tested on its own; the next phase does not need it, but a test reads it |

## Deviations & follow-ups

- None against the plan.
- Pre-existing, in the baseline: `HistoryDragMergeTests.TheMerge_RecordsAMergeCommitOnTheDestination`
  is flaky in the full Desktop suite. It failed once with an `ObjectDisposedException` from
  `RepositoryContext.EndWrite`: an earlier test's checkout finished on the shared dispatcher after its
  services were disposed. It passed when run alone, and in the next full run. Left alone (not this
  run's subject).

## Build/test evidence

- **Run baseline** (run branch `feature/2026-11-05-history-badges-release` @ `16ba252`, before any dev
  branch), which later devs cite:
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - `dotnet test --solution Enigma.GitClient.slnx`: 3,577 total, 3 skipped. The first run had 1
    failure, the flaky test above; the second run had 0 failures.
- This dev:
  - `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
  - `dotnet test --solution Enigma.GitClient.slnx`: 3,582 total, 3,579 passed, 3 skipped,
    **0 failed**.
  - New tests (`HistoryBadgeOrderTests`, 5): the comparer on a shuffled list; names ignoring case; a
    joined badge counts as local; a line built from refs out of order; the order against real git with
    a bare `origin`.
- Fix budget: 0 cycles used.

## Documentation sweep

- README: its badge lines (25, 49–89) describe what badges offer, not their order. Nothing became
  wrong; no edit.
