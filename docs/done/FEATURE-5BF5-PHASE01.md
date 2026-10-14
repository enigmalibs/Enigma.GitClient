# FEATURE-5BF5-PHASE01 — A lease pinned to a commit

**Item:** FEATURE-5BF5 — Force push a rejected branch
**Phase:** PHASE01 — A lease pinned to a commit
**Branch:** `feature/feature-5bf5-phase01-pinned-lease`
**Run:** feature/2026-10-14-home-search-force-push

## Summary

A forced push can now be pinned to the exact commit the user was shown, so it replaces that commit
on the remote or nothing at all.

- **`PushRequest.ExpectedRemoteTip`** (`string?`): the full object name of the remote branch's
  commit the push may replace. It has no effect without `ForceWithLease`.
- **`SyncService.BuildPushArguments`**: with `ForceWithLease` and a tip, it passes
  `--force-with-lease=refs/heads/<Branch>:<tip>` instead of the bare `--force-with-lease`.
  - The pinned form needs a named branch and a full object name (40 or 64 hex digits); anything else
    is an `ArgumentException`, so the value can never be read as something other than a commit.
  - A delete still never forces, and a bare `--force` is still never produced.
- **Why pinned.** git's own lease is the remote-tracking branch, which every fetch moves — and the
  app fetches in the background every 60 s. A push that relies on it can replace commits that arrived
  while the user was reading the warning. Pinned, the push replaces exactly what the warning
  described: if the remote moved in between, it is refused with git's *stale info*, which
  `SyncErrorMapper` already reports as `StaleLease`.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Core/Sync/SyncService.cs`: `PushRequest.ExpectedRemoteTip`; the pinned
    lease in `BuildPushArguments`; `BuildPinnedLease`.
  - `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncParsingTests.cs`:
    - the pinned argument (SHA-1 and SHA-256);
    - malformed tips and a missing branch throw;
    - a tip without a force forces nothing;
    - a delete ignores the lease.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Sync/SyncServiceTests.cs`:
    - a pinned lease replaces the remote while it is at that commit;
    - it is refused as `StaleLease` once the remote moved, even after a fetch moved the
      remote-tracking branch.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| Which ref the lease names | `refs/heads/<Branch>`, the push's own destination | `git push <remote> <branch>` updates the remote ref of the same name; leasing that ref is leasing what the push writes | The upstream's name (differs only for a branch tracking a renamed remote branch, where the push itself goes elsewhere) |
| Validating the tip | Full object name only, 40 or 64 hex digits | The lease compares object names; an abbreviation or a ref name would make git resolve it on its own terms | Accepting any revision |
| Where the validation lives | `BuildPushArguments`, the public builder every push goes through | A malformed request fails before a process starts, and the unit tests reach it directly | Validating in `PushAsync` only |

## Deviations & follow-ups

- No deviation from the plan.
- The README's *Remotes* bullet ("push (with `--force-with-lease`)") is still accurate after this
  phase. PHASE02, which makes the force reachable from the app, updates it.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- Core suites:
  - `Enigma.GitClient.Core.UnitTests`: **1183 passed, 1 skipped, 0 failed** (10 new cases).
  - `Enigma.GitClient.Core.IntegrationTests`: **361 passed, 2 skipped, 0 failed** (2 new tests;
    3 min 55 s).
- No Desktop code changed in this phase.
- **Re-run on Linux** when the run was committed: `dotnet build` **0 warnings, 0 errors**;
  `Enigma.GitClient.Core.UnitTests` **1183 passed, 1 skipped**; `Enigma.GitClient.Core.IntegrationTests`
  **363 passed** (the two skipped on Windows run here); `Enigma.GitClient.Desktop.UnitTests`
  **1519 passed, 0 failed**.
