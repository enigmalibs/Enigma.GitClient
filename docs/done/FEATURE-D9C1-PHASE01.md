# FEATURE-D9C1-PHASE01 — Bulk local identity in Core

**Item:** FEATURE-D9C1 — The profile's identity in every repository
**Branch:** `feature/feature-d9c1-phase01-bulk-local-identity`
**Run:** feature/2026-10-27-repositories-local-identity

## Summary

`IBulkLocalIdentityService.SetLocalAsync(workTreePaths, identity)` gives several repositories the same
identity of their own, the way `IGitIdentityService.SetLocalAsync` gives it to one:

- the identity is validated once, trimmed, before any repository is read — an invalid one throws
  `ArgumentException` and nothing is written;
- each path, in order, is discovered; a blank path, a missing folder, a plain folder, a bare repository,
  or a folder whose discovered work tree is not the path itself (a folder inside another repository) is
  `NotARepository` and nothing is written there;
- a repository whose own identity already is this one (`GitIdentity.IsSameAs`) is `AlreadySet` and not
  rewritten — two worktrees sharing one configuration are written once;
- otherwise `Written`; a `GitCommandException`, `IOException` or `UnauthorizedAccessException` is
  `Failed` with the exception, and the next repository is tried; `GitNotFoundException` and a
  cancellation propagate;
- one `BulkLocalIdentityOutcome(Path, Result, Failure)` per path, in the order given.

## Files/modules touched

**Created — Core**

- `src/Enigma.GitClient.Core/Identity/BulkLocalIdentityService.cs`

**Modified — Core**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Identity/BulkLocalIdentityServiceTests.cs`
- `tests/Enigma.GitClient.Core.IntegrationTests/Identity/BulkLocalIdentityServiceTests.cs`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Comparing the listed path with the discovered work tree | Full path, trailing separator trimmed, case-insensitive on Windows — `RepositoryHandle`'s own rule | A list entry written with a trailing separator is still its repository; the handle already normalises git's output the same way |
| A blank path | `NotARepository`, git not run | The locator refuses a blank path with an exception; one bad entry must not stop the rest |
| The pre-existing `TokenStoreTests` failure | Recorded, not fixed; the dev is not held red by it | It fails identically at the run's starting commit `8b197c4` (verified on a `git archive` of it); unrelated to this dev — FEATURE-9A00 recorded the same |

## Deviations & follow-ups

- None from the plan.
- **Pre-existing failure (unchanged):** `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows at the starting commit too. Follow-up already noted by FEATURE-9A00: a BUG item for
  the test (BUG-3024's area).
- Recommendation only: nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: the service has no user-facing surface yet; PHASE02 adds the button and the README lines.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Core.UnitTests`: 1392 total, 1389 passed, 1 failed (the pre-existing token-store test above),
  2 skipped; 20 new, all passing.
- `Core.IntegrationTests`: 414 total, 412 passed, 0 failed, 2 skipped (Windows file modes); 4 new, all passing.
- `Desktop.UnitTests`: compiled with the solution, not run on this Windows machine (house rule).
- Fix budget: 0 cycles used.
