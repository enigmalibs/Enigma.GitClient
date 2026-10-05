# FEATURE-659E-PHASE01 — The revert engine

**Item:** FEATURE-659E — Revert a commit from the history
**Branch:** `feature/feature-659e-phase01-revert-engine`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

`IRevertService.RevertAsync(repository, revision, mainline, token)` records a commit on the branch HEAD
is on that undoes another commit — `git revert --no-edit [--mainline n] <revision> --`, committed with
git's own message (`Revert "<subject>"` / `This reverts commit <sha>.`). A merge is reverted against the
parent the caller names (the history will pass 1, the branch it was merged into).

It reports a `RevertOutcome` rather than throwing, because every way a revert stops is a result:
`Reverted` (with the new commit's sha), `NothingToRevert` (the change is already undone),
`Conflicted` (with the files, read from the status) and `Failed` (work in the way, a merge with no
parent named, or git's first line). A revert is **never left in progress**: whenever git leaves
`REVERT_HEAD` behind — a conflict, or anything else — the service runs `git revert --abort`, which puts
the branch and the files back and keeps unrelated local edits. The app has nothing that could finish or
abandon a revert, and one left behind would block the reset and the next revert.

## Files / modules touched

**Created — Core**

- `Revert/RevertService.cs` — `RevertResultKind`, `RevertOutcome`, `IRevertService`, `RevertService`
  (`BuildArguments`, `Classify`, `RevertAsync`)

**Modified — Core**

- `DependencyInjection/ServiceCollectionExtensions.cs` — `IRevertService` registered beside
  `IResetService`

**Created — tests**

- `Core.UnitTests/Revert/RevertArgumentsTests.cs` — the vector with and without a mainline ends in
  `--`; dash-led, blank revisions and a mainline below 1 are refused; `Classify` reads a success, a
  conflict, nothing to commit, both "local changes" wordings, a merge with no `-m`, and anything else as
  git's first line (16 tests)
- `Core.IntegrationTests/Revert/RevertServiceTests.cs` — real git: the revert commit, its message and
  parent; a merge reverted with mainline 1; a conflict abandoned with the file named and nothing left
  behind; a conflict keeping an unrelated edit; reverting what is already undone; staged work in the
  way; an unrelated unstaged edit kept (7 tests)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A dash-led revision | `ArgumentException`, as `ResetService.BuildArguments` does | The plan's wording; the sibling's precedent |
| Where git's wording is read | `Classify` over standard output and error together | git writes "nothing to commit" to standard output and the refusals to standard error |
| A failure other than a conflict that leaves `REVERT_HEAD` | Aborted too | The plan's rule — never leave a revert in progress — whatever the cause |
| The conflicted files | Read from the status before the abort | Afterwards there is nothing left to name; the status is the merge engine's source too |

## Deviations & follow-ups

- None from the plan.
- A revert cancelled while git is running (the repository closing under it) could leave `REVERT_HEAD`;
  the main window's banner already names a revert in progress. Not handled: the menu's revert runs to
  completion under the repository lock.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: the engine has no user-facing surface yet; PHASE02's menu item is where README
  mentions the revert.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2754 passed, 0 failed (23 new; baseline 2731).
- Fix budget: 0 cycles used.
