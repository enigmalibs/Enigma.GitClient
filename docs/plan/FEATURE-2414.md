# FEATURE-2414 — Faster integration test suite

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** vibe/2026-10-08-slow-hanging-tests

## Objective

`Enigma.GitClient.Core.IntegrationTests` spends its time starting git processes. Remove the starts
that buy nothing, without weakening any test's isolation or assertion.

## Context & constraints

- **Baseline, 2026-10-08, this Windows machine (14 threads):** 358 tests in **3m45s**.
  - Run alone, `RefReaderTests` costs about 2 s a test.
  - In the full run, the same tests take 11–18 s each, from contention.
- **The cost is process creation, and it barely parallelizes here.**
  - Measured from .NET: 66 ms per `git` start serially, and 43 ms effective with 14 starts at a time.
  - Wall time is therefore about (git starts × ~40 ms).
  - The test runs here use `Git\cmd\git.exe`, a wrapper that starts a second process. That raises the
    cost per start again: 48 ms serially for `mingw64\bin\git.exe`.
- **A `GIT_TRACE2` run counted 7,054 git starts:**
  - **920 (13%) are `git maintenance run --auto [--no-quiet] --detach`.** git forks one after every
    commit, merge, fetch or push, and it is pointless in a throwaway repository.
  - About **1,485 (~21%) rebuild the same `HistoryFixture`**: about 33 starts × 45 tests, in
    `RefReaderTests` and `CommitLogReaderTests`. The history is deterministic: explicit dates and a
    fixed identity give the same SHAs on every build.
  - 822 are fixture `rev-parse HEAD` calls after each commit, and 783 are `init` + `symbolic-ref`.
    These are deliberately left alone: the fixture's minimal, version-honest git use is a design
    choice.
- Every workspace already isolates git. `GitWorkspace.Create` writes an empty global config and an
  empty system config, and points `GIT_CONFIG_GLOBAL` / `GIT_CONFIG_SYSTEM` at them. Both the fixture
  runner and the product runner (through `EnvironmentOverrides`) use that environment.
  - `GitIdentityServiceTests.SetGlobal_RefusesAnInvalidIdentityBeforeGitRuns` asserts that the
    **global** file is empty. No test reads the system file.
- Tests change the history repository (stash, push, checkout, tag, merge…), so each test needs its
  own copy.

## PHASE01 — No git auto-maintenance in tests

**Branch:** `feature/feature-2414-phase01-no-auto-maintenance`
**Status:** DONE — see `docs/done/FEATURE-2414-PHASE01.md`

### Steps

1. `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/GitWorkspace.cs`: the isolated system
   config carries `maintenance.auto = false` (git 2.29+, which then forks nothing) and `gc.auto = 0`
   (older git's `gc --auto` returns at once). One comment explains why.
2. Measure: run the full integration suite, and count git starts again with `GIT_TRACE2`.

### Acceptance criteria

- No `git maintenance` start appears in a traced run of the integration suite.
- The integration suite is green, with the same totals (358, 2 skipped), and its wall time is lower
  than the 3m45s baseline. The figure is recorded in the completion doc.
- Build: zero warnings. The Core unit suite is green. The Desktop suite is compiled, not run.

## PHASE02 — History fixture built once, copied

**Branch:** `feature/feature-2414-phase02-history-template`
**Status:** TODO

### Steps

1. `Infrastructure/HistoryTemplate.cs` (new): an xUnit v3 assembly fixture.
   - Its `InitializeAsync` builds the `HistoryFixture` history once, in a workspace of its own.
   - Its `DisposeAsync` deletes that workspace.
   - It is registered with `[assembly: AssemblyFixture(...)]`.
2. `HistoryFixture`: a per-test fixture comes from copying the template's repository directory into
   the test's own workspace. The template's SHAs carry over.
   - The builder stays as the single description of the history.
   - The copy keeps git's read-only object files readable, and needs no git start.
3. `RefReaderTests` and `CommitLogReaderTests` take the template through their constructor, and build
   their fixture from it in `InitializeAsync`. Their tests are unchanged.
4. Measure the full integration suite again.

### Acceptance criteria

- Each test still gets its own repository: a test that changes it cannot affect another.
- Every `RefReaderTests` and `CommitLogReaderTests` test passes unchanged.
- The integration suite is green with the same totals, and its wall time is lower than after PHASE01.
  The figure is recorded in the completion doc.
- Build: zero warnings. The Core unit suite is green. The Desktop suite is compiled, not run.

## Out of scope

- The Desktop test suite: it must not run on this machine. It also runs git with the developer's own
  configuration, with no isolated workspace to put a setting in.
- Bypassing the `Git\cmd\git.exe` wrapper. That is the product's executable resolution, which credential
  helpers and ssh rely on.
- Fewer fixture git calls per commit (`rev-parse HEAD`) or per init (`symbolic-ref`).
- Changing the parallelization mode or thread count: process creation is the bottleneck, and it
  barely parallelizes.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Speed up the suite at all | Yes: the two largest measured, avoidable costs | You said the tests take a really long time; the trace names the waste | Only fixing the hang (leaves 3m45s) |
| Where `maintenance.auto=false` goes | The workspace's isolated system config | Applies to fixture and product git alike; the global file is asserted empty by an identity test | The global config (breaks that test); `GIT_CONFIG_COUNT` env vars (outrank every file a test writes) |
| Also `gc.auto=0` | Yes | git before 2.29 runs `gc --auto` instead; the product supports 2.20+ | `maintenance.auto` alone |
| How to share the history | Build once in an assembly fixture, copy per test | Tests change the repository, so sharing one is unsafe; a file copy costs milliseconds against ~33 git starts | An `IClassFixture` shared repository (breaks isolation); a static cache (leaves a temp directory behind every run) |
| The other fixture git calls | Left as they are | They are the fixture's deliberate, version-honest git use; changing them churns every test file | Reading SHAs from `.git` files; `git init -b` |
| Parallelism tuning | None | Measured: process creation is serialized, so more or fewer threads move little | `Parallelization(MaxThreads = …)` |
