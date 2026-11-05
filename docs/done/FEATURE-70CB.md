# FEATURE-70CB — Delete a branch and its remote at once

**Item:** FEATURE-70CB — Delete a branch and its remote at once
**Branch:** `feature/feature-70cb-delete-local-and-remote`
**Run:** feature/2026-11-05-history-badges-release

## Summary

A History badge that stands for a local branch and its upstream (both on the same commit) offers
*Delete "x" and "origin/x"…*, right after *Delete "x"…* and *Delete "origin/x"…*. It goes through the
existing batch delete, `IBranchOperations.DeleteAsync(IReadOnlyList<BranchToDelete>)`:

- one question naming both branches, the commits at risk and the remote warning;
- both deletes;
- one reading of the repository and one report;
- then the history reloads.

It is disabled on the checked-out branch, which git will not delete, and hidden on every badge that
does not draw both.

## Files/modules touched

- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchViewModel.cs`: the
    `DeleteWithRemote` command, `CanDeleteWithRemote`, `DeleteWithRemoteHeader`.
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: the command,
    `OnDeleteBranchWithRemoteAsync`, notified with the other branch commands.
  - `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the menu item.
  - `docs/roadmap.md`, `docs/plan/FEATURE-70CB.md`: statuses.
- Created:
  - `tests/Enigma.GitClient.Desktop.UnitTests/HistoryDeleteBothTests.cs`
  - `docs/done/FEATURE-70CB.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Can-execute | `CanDeleteWithRemote && CanDelete` | `CanDelete` is the single local delete's rule (not the checked-out branch), so the two agree |
| Re-evaluation | Notified in `NotifyBranchCommands` with the others | HEAD moving changes which badge is the checked-out one |

## Deviations & follow-ups

- None against the plan.

## Build/test evidence

- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx`: **0 warnings, 0 errors**.
- `dotnet test --solution Enigma.GitClient.slnx`: 3,606 total, 3,603 passed, 3 skipped, **0 failed**.
- New tests (`HistoryDeleteBothTests`, 5, real git with a bare `origin`):
  - a joined badge shows the three deletes in order, the new one bound to `DeleteWithRemote` with the
    branch, glyph `Trash`;
  - a local-only badge and a remote-only badge do not show it, and it cannot execute there;
  - disabled on the checked-out branch;
  - confirming asks one question, deletes the branch here and on `origin`, and the reloaded history
    draws neither;
  - cancelling deletes neither.
- Fix budget: 0 cycles.

## Documentation sweep

- README line 48 ("Branch management — create, rename, delete, …") stays true; no edit.
