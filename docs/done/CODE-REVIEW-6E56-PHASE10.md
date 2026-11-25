# CODE-REVIEW-6E56-PHASE10 — [Medium] SOLID-05 One git run sequence

## Summary

`GitProcessRunner` carried three copies of the same sequence, one for text, one for raw bytes and
one for streamed standard error:

1. start;
2. begin reading both outputs;
3. write standard input;
4. await the readers and the exit;
5. log the completion;
6. kill on cancellation.

A fix to the shared part had to be made three times.

- One private `RunProcessAsync<TOutput>(command, readOutput, readError, cancellationToken)` now does
  it, returning a private `GitRun<TOutput>` (exit code, outputs, duration).
- `RunAsync` passes the text readers. `RunRawAsync` passes a byte reader (`ReadOutputBytesAsync`).
  `RunStreamingAsync` passes the chunked standard-error reader that reports progress.
- `ToResult` shapes a text run into a `GitResult` or the `GitCommandException`, for both text entry
  points.
- Logging, redaction, the forbidden-operation check, the exception types and PHASE08's inner
  exception are unchanged. `KillQuietly` is now called from one place, where it was called from
  three.

## Files/modules touched

- Modified: `src/Enigma.GitClient.Core/Git/GitProcessRunner.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE10.md`

## Deviations & follow-ups

- None from the plan.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - build: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 51 s).
  - Desktop: compiled, **not run** (untouched by this dev).
- **Tests:** none added, as the plan says: the change is behaviour-preserving, and the existing tests
  are the contract.
  - `GitProcessRunnerTests` (integration) covers text output, failure with and without throwing, the
    argument vector, raw UTF-8 paths, cancellation, raw bytes, NUL-split lines, the rebase refusal
    and standard input.
  - The streaming path is exercised by the clone, fetch and submodule integration tests.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Shape of the shared run | A generic method with two reader delegates, returning a private record struct | The three variants differ only in how the outputs are read | An abstract reader class per variant (more types for three lambdas) |
