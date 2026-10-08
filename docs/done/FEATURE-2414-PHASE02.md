# FEATURE-2414-PHASE02 — History fixture built once, copied

**Item:** FEATURE-2414 — Faster integration test suite, PHASE02
**Branch:** `feature/feature-2414-phase02-history-template`
**Run:** vibe/2026-10-08-slow-hanging-tests

## Summary

The `HistoryFixture` history is now built once per test run and **copied** into each test's
workspace.

- Before, `RefReaderTests` and `CommitLogReaderTests` rebuilt it for each of their 45 tests, about
  30 git starts each time.
- Those two collections were among the longest of the run. `CommitLogReaderTests` started late and
  finished last, so it set the suite's wall time.

**Measured, as an interleaved A/B on the full integration suite.** The baseline is the run branch with
PHASE01, built from a `git archive` of it in a scratch directory.

| Run | Baseline | PHASE02 |
|---|---|---|
| Round 1 | 6m03s | 5m03s |
| Round 2 | 5m56s | 4m46s |
| **Mean** | **5m59s** | **4m54s (−18%)** |

- Sum of all test times: about 3,550 s → about 2,750 s (−22%).
- In the full run, `RefReaderTests` drops from 303–312 s to 89–93 s, and `CommitLogReaderTests` from
  277–283 s to 85–92 s.
- Each class run alone: `RefReaderTests` drops from 40–48 s to 14.6 s, and `CommitLogReaderTests`
  from 76.8 s to 21.5 s. Both figures include the one template build.
- The slowest collection is now `BranchServiceTests`. No history class is on the critical path any
  more.

**How:**

- `HistoryTemplate` (new) is an xUnit v3 assembly fixture, `[assembly: AssemblyFixture<HistoryTemplate>]`.
  - Its `InitializeAsync` builds the history with the unchanged `HistoryFixture.CreateAsync`, in a
    workspace of its own.
  - Its `DisposeAsync` deletes that workspace.
- `HistoryFixture.CopyInto(workspace, name)` copies the repository directory: every directory first,
  empty ones included (git needs `.git/refs`), then every file. The new fixture carries the same
  SHAs. A copy starts no git process.
- `RefReaderTests` and `CommitLogReaderTests` take the template through their constructor, and call
  `_template.CopyInto(_workspace)` where they called `HistoryFixture.CreateAsync(_workspace)`. Their
  tests are unchanged.
- `HistoryTemplateTests` (new) proves the isolation the two classes rely on:
  - a copy is a clean repository on `main` at the last commit (`git status` stays empty even though
    the index's stat data came from another directory);
  - a copy a test commits to leaves the next copy as it was.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/HistoryTemplate.cs`
- `tests/Enigma.GitClient.Core.IntegrationTests/HistoryTemplateTests.cs`
- `docs/done/FEATURE-2414-PHASE02.md`

**Modified**

- `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/HistoryFixture.cs`: `CopyInto` and
  `CopyDirectory`.
- `tests/Enigma.GitClient.Core.IntegrationTests/Refs/RefReaderTests.cs`: the template, injected and
  copied.
- `tests/Enigma.GitClient.Core.IntegrationTests/History/CommitLogReaderTests.cs`: the same.
- `docs/roadmap.md`, `docs/plan/FEATURE-2414.md`: statuses. The item is `DONE` with this, its final
  phase.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The assembly-fixture attribute | The generic `AssemblyFixture<HistoryTemplate>`, at the top of `HistoryTemplate.cs` | It exists in xunit.v3 4.0.1 (checked in its XML docs), and it sits beside the type it registers |
| Where the copy lives | `HistoryFixture.CopyInto`, with the template delegating to it | The fixture owns its repository and its SHAs. The template only owns the build's lifetime |
| Refreshing the index after a copy | Nothing | git notices the changed stat data and re-hashes the files. `ACopy_IsACleanRepositoryOnMainAtTheLastCommit` proves the copy reads as clean |
| Proving isolation | Two tests in `HistoryTemplateTests`, at the project root beside `GitAvailabilityTests` | The acceptance criterion "a test that changes it cannot affect another" needed a test of its own. `Infrastructure/` holds helpers, not tests |
| How to get a fair baseline | `git archive` of the run branch into the scratchpad, built there, timed interleaved with this branch | Leaves the repository untouched (no stash, no worktree), and interleaving cancels the machine's drifting load |

## Deviations & follow-ups

- **No deviation from the plan,** apart from the added `HistoryTemplateTests`.
- **Cost of the shared template:** an assembly fixture is built at the start of every run of this
  assembly, even a run filtered to unrelated classes. That costs about 30 git starts, roughly 1.5 s.
- **Next candidates, not acted on.** The longest collections are now `BranchServiceTests`,
  `MergeServiceTests` and `SyncServiceTests`, each 200–300 s under contention.
  - They build small repositories inline, test by test, so a shared template does not fit them as it
    stands.
  - The fixture's `rev-parse HEAD` after every commit (822 starts) and `init` + `symbolic-ref` (783)
    are the next-largest costs. The plan deliberately left them alone.
- **Machine noise.** This afternoon's absolute times, 4m45s–6m03s, are well above this morning's
  3m45s for the same baseline code. Only the interleaved comparison is meaningful.

## Documentation sweep

Nothing stale: no human-facing doc describes the integration fixtures.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1169 total, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: **360 total** (2 new), 0 failed, 2 skipped (4m41s). The
  two A/B runs of this branch before the new tests were also green: 358 total, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your standing instruction.
- Fix budget: 0 cycles.
