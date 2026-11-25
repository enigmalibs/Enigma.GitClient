# CODE-REVIEW-6E56-PHASE08 — [Medium] EXC-02 Git start keeps its cause

## Summary

A `Win32Exception` from starting git was reduced to its message inside a new `GitNotFoundException`,
whose only constructor took a message. The native error code and the stack were lost: "access
denied" on the executable, a bad working directory and a missing file all logged as the same
exception, with no inner one.

- `GitNotFoundException` has the standard constructors: parameterless (a generic message),
  `(string)` and `(string, Exception?)`.
- `GitProcessRunner` passes the `Win32Exception` as the inner exception. The message is unchanged.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Core/Git/GitNotFoundException.cs`
- Modified: `src/Enigma.GitClient.Core/Git/GitProcessRunner.cs`
- Created: `tests/Enigma.GitClient.Core.UnitTests/Git/GitNotFoundExceptionTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE08.md`

## Deviations & follow-ups

- None from the plan. PHASE10 (SOLID-05) reshapes the same runner, and keeps this catch as it is.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`. Since PHASE07, the Core unit suite is
  expected fully green.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1532 run, 1529 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (7 min 43 s).
  - Desktop: compiled, **not run** (untouched by this dev).
- **New tests (Core, run):** `GitNotFoundExceptionTests` (2):
  - the three constructors;
  - a runner pointed at a rooted path where nothing is throws `GitNotFoundException`, whose
    `InnerException` is the `Win32Exception` with a non-zero native code.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes this exception.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the test lives | Core unit tests, with a real runner and a missing rooted executable | The start fails in the operating system without any git installed | An integration test (needs nothing the unit suite lacks) |
