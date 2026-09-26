# FEATURE-1A7E-PHASE02 — Soft and hard reset in the line menu

**Item:** FEATURE-1A7E — Reset the branch to a commit
**Branch:** `feature/feature-1a7e-phase02-reset-menu`
**Run:** feature/2026-09-23-history-branch-reset

## Summary

Every line of the history now offers, right after "Check out this commit (detaches HEAD)":

- **`Reset "main" to this commit - Soft (keep all changes)`** — moves the branch HEAD is on to the
  line's commit and keeps everything, staged. It asks nothing: nothing is lost.
- **`Reset "main" to this commit - Hard (discard all changes)`** — asks first ("Discard all
  changes?", Cancel the default), naming where the branch will point and the tracked files whose
  uncommitted changes are thrown away, and saying that untracked files are left alone. Confirmed, it
  moves the branch and makes the tracked files that commit's.

The branch is named after the one HEAD is on. On a detached or unborn HEAD neither item is offered.
They are greyed out on the uncommitted row (no commit), and Soft is greyed out on the line HEAD is
already at (it would do nothing) — Hard stays, since that is how the uncommitted work is thrown away.

A reset is refused, with a sentence in the info bar and git never asked, when HEAD has left the branch
the menu named (the item carries that branch), or while a merge, cherry-pick, revert, bisect, rebase
or `am` session is in progress. After a reset the history is read again and the info bar says where
the branch is.

## Files / modules touched

**Created — App**

- `Services/ResetOperations.cs` — `IResetOperations`, `ResetOperations`: the refusals (`Refuse`), the
  hard-reset confirmation, the write under `RunExclusiveAsync`, the report

**Modified — App**

- `DependencyInjection/ServiceCollectionExtensions.cs` — `IResetOperations` registered as a singleton
- `ViewModels/Pages/CommitRowViewModel.cs` — `HistoryRowCommands` gains `ResetSoft`, `ResetHard` and
  `CurrentBranch`; `HistoryResetRequest` (the line and the branch the item named); `ResetBranch`,
  `ResetHeader`; the two entries in `MenuEntries`
- `ViewModels/Pages/HistoryPageViewModel.cs` — takes `IResetOperations`; the two commands and their
  can-execute; `OnResetAsync`, which re-reads the history after a reset
- `README.md` — the feature list mentions the reset (sweep)
- `RELEASENOTES.md` — "The graph" mentions the reset (sweep)

**Tests**

- `App.UnitTests/HistoryResetTests.cs` — the menu names the current branch in both items right after
  the checkout; nothing on a detached HEAD; greyed out on the uncommitted row, Soft greyed out on the
  HEAD row; Soft moves the branch, keeps the work staged, asks nothing and reloads; Hard asks, and
  Cancel changes nothing; confirmed Hard discards the tracked change, keeps the untracked file and
  names only the file it takes; refused when HEAD has left the named branch; refused during a merge
  (no question, `MERGE_HEAD` still there); `Refuse` for every HEAD state and operation

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What the reset commands take | `HistoryResetRequest(Row, Branch)` built when the menu is, not the row | The plan's guard ("refuse when HEAD is no longer on the branch the menu named") needs the named branch; reading it again on click would read the new branch and defeat the guard |
| The two headers | One static `CommitRowViewModel.ResetHeader(branch, mode)` rather than two properties | The header and the request carry the same branch, read once per menu build |
| Where HEAD is read for the refusals | `IRepositoryContext.Head`, as the other operations read it | The context's state is what the menu was built from; the automatic refresh keeps it current, and a re-read before every reset would announce a state refresh for nothing |
| The success sentences | Soft: "Every change is kept, and staged: commit it again whenever you are ready." Hard: "The tracked files are as that commit left them." | True whether or not the commit is an ancestor of the branch |
| The file list's cap | `CheckoutOperations.MaximumListedFiles` (12), then "…and N more" | The same list, the same length, as the checkout's discard question |

## Deviations & follow-ups

- The plan named the commands `AsyncRelayCommand<CommitRowViewModel>`; they take a
  `HistoryResetRequest` instead (see above).
- `HistoryPageViewModel` now takes 17 dependencies. The follow-up already recorded by
  FEATURE-3507-PHASE02 — moving the row and branch handlers into a dedicated history-actions service —
  applies with one more reason.
- `Describe`, `CountFiles` and `FirstLine` now exist in both `CheckoutOperations` and
  `ResetOperations` (`FirstLine` in others too); a shared internal helper would remove the copies.
- Follow-up: resetting a branch behind its upstream leaves it needing a force-push, which the client
  does not offer; a warning, or `--force-with-lease` from the badge menu, would close that loop.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1955 passed, 0 failed (18 new), first run after
  the build's one compile fix (a local name clash, before any test ran).
