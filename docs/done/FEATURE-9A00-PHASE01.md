# FEATURE-9A00-PHASE01 — The squash engine

**Item:** FEATURE-9A00 — Squash a range of commits onto a new branch
**Branch:** `feature/feature-9a00-phase01-squash-engine`
**Run:** feature/2026-10-27-feature-9a00

## Summary

`ISquashService` replaces the commits between a base and a tip with one commit, on a new branch, and
changes nothing that exists:

- `PreviewAsync(repository, base, tip)` describes the range: the commits that descend from the base and
  lead to the tip (`log --ancestry-path`), how many are merges, the non-merge subjects oldest first,
  the merges that bring in a branch from outside the range (a parent neither on the path nor the base)
  with the number of commits they bring, and whether the tip's files are the base's.
- `SquashAsync(repository, request)` writes the commit with `git commit-tree <tip>^{tree} -p <base>
  -F <file>` — no ref, index or work tree touched — then creates the branch on it through
  `IBranchService.CreateAsync(name, sha, checkout)`. A switch blocked by local changes leaves no branch
  (`checkout -b` checks the work tree first) and comes back as a `GitOperationRefusedException` naming
  the files and saying nothing was created.
- Every refusal comes before anything is written: the base as the tip, a base off the tip's
  first-parent line (or no ancestor at all, each with its own sentence), a blank title, an invalid or
  taken branch name, an unknown revision.
- `SquashRange.Measure(newestFirst, base, tip)` gives the History menu the same count from the loaded
  commits, without git, relying on the history's order (a commit always above its parents).

## Files/modules touched

**Created — Core**

- `src/Enigma.GitClient.Core/Squash/SquashService.cs`
- `src/Enigma.GitClient.Core/Squash/SquashRange.cs`

**Modified — Core**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Squash/SquashArgumentsTests.cs`
- `tests/Enigma.GitClient.Core.UnitTests/Squash/SquashRangeTests.cs`
- `tests/Enigma.GitClient.Core.IntegrationTests/Squash/SquashServiceTests.cs`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Message cleanup | `SquashService.CleanUpMessage`: LF endings, no trailing spaces, no run of blank lines, one final newline | `git commit` does this by default and `commit-tree` records bytes verbatim; the plan's `CommitService.Normalise` alone would keep trailing spaces |
| Which line is the title | The first line as typed (`TitleOf`); blank → refused | The dialog starts with an empty title line; skipping blank lines would promote the first bullet to the title |
| First-parent check | Some commit of `rev-list --first-parent --parents base..tip` has the base as its first parent | Exact, and independent of the output's order under clock skew |
| Revisions | Resolved to full shas (`rev-parse --verify --quiet <rev>^{commit}`) before any comparison; dash-led ones throw `ArgumentException` | The path's parents are full shas; the revert's precedent for dashes |
| Name taken | Checked before `commit-tree` | No unreferenced commit is written for a squash that cannot finish |
| Blocked switch sentence | Names the files git lists after a tab, UI-neutral ("create the branch without switching to it") | The Core does not know the dialog's wording |
| A unit test that fails on the starting commit | Recorded below, not fixed | Unrelated to this dev, identical before and after it; fixing it here would be a drive-by |

## Deviations & follow-ups

- Plan step 1 said `CommitService.Normalise`; the message goes through `CleanUpMessage` instead (see
  above), which normalises line endings as well.
- **Pre-existing failure:** `Core.UnitTests.Security.TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`
  fails on Windows at the starting commit `develop @ 3f32e23` too (verified by building a `git archive`
  of it: 1 failed of 1340). The `RefusingProtector` store writes the reset key with its fake
  protection, which the real DPAPI store cannot open. Follow-up: a BUG item for the test (BUG-3024's
  area).
- Recommendation only: line endings were not examined beyond this diff; nothing in it showed CRLF churn.

## Documentation sweep

- Nothing to change: the engine has no user-facing surface yet; PHASE02 adds the menu and README entry.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Core.UnitTests`: 1372 total, 1369 passed, 1 failed (the pre-existing token-store test above),
  2 skipped; 32 new, all passing.
- `Core.IntegrationTests`: 410 total, 408 passed, 0 failed, 2 skipped (Windows file modes); 19 new,
  all passing.
- `Desktop.UnitTests`: compiled with the solution, not run on this Windows machine (house rule).
- Fix budget: 0 cycles used.
