# FEATURE-1A7E-PHASE01 — The reset engine

**Item:** FEATURE-1A7E — Reset the branch to a commit
**Branch:** `feature/feature-1a7e-phase01-reset-engine`
**Run:** feature/2026-09-23-history-branch-reset

## Summary

The git engine can now move the branch HEAD is on to another commit, which PHASE02 offers from the
history's line menu.

- `IResetService.ResetAsync(repository, revision, mode, token)` runs `git reset --soft|--hard
  <revision> --`.
- `ResetMode.Soft` moves the branch only: the index and the work tree stay as they were, so what the
  later commits changed shows as staged.
- `ResetMode.Hard` also makes the index and the tracked files the commit's; untracked files are left
  alone, as git leaves them.
- `ResetService.BuildArguments` refuses a blank revision, a revision that starts with a dash (it would
  be read as an option — `--end-of-options` needs git 2.24, above the supported 2.20) and a mode that
  does not exist. The trailing `--` makes a revision that is also a file's name read as the commit.

The service only does what it is told. Whether a reset should happen — detached HEAD, a merge in
progress, work about to be lost — is the App's question, and PHASE02's.

## Files / modules touched

**Created — Core**

- `Reset/ResetService.cs` — `ResetMode`, `IResetService`, `ResetService`, `BuildArguments`

**Modified — Core**

- `DependencyInjection/ServiceCollectionExtensions.cs` — `IResetService` registered as a singleton

**Tests**

- `Core.UnitTests/Reset/ResetArgumentsTests.cs` — each mode's vector ends the revisions with `--`; a
  dash-led, a blank revision and an undefined mode are refused
- `Core.IntegrationTests/Reset/ResetServiceTests.cs` — soft moves the branch and keeps the changes
  staged; soft keeps uncommitted work exactly as it was; hard makes the tracked files the commit's and
  keeps an untracked file; hard to HEAD throws away only the uncommitted work; a commit that is not an
  ancestor; a revision that is also a file name

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What a dash-led revision throws | `ArgumentException`, not the plan's `GitOperationRefusedException` | The precedent: `SyncService.BuildFastForwardArguments` throws `ArgumentException` for a dash-led remote. It is a caller's mistake (the history passes a full sha), not a refusal to show the reader |
| What `ResetAsync` returns | Nothing (`Task`) | The caller reads HEAD from the context's refresh after `RunExclusiveAsync`; a second read here would be redundant |
| Dependencies | The command factory and the process runner only | Nothing else is needed; no ref reader, unlike `CheckoutService`, for the reason above |

## Deviations & follow-ups

- The dash-led revision throws `ArgumentException` rather than `GitOperationRefusedException` (see
  above); the acceptance criterion — nothing starting with `-` reaches git — holds.
- Documentation sweep: nothing to change — the engine is not visible to the user until PHASE02.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1937 passed, 0 failed (14 new), first run.
