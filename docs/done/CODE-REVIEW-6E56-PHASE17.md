# CODE-REVIEW-6E56-PHASE17 — [Medium] TEST-03 No fixed test delays

## Summary

Five tests waited a fixed time, 50, 100 or 300 ms. Three then asserted that nothing had happened; two
relied on 50 ms being long enough for a writer to meet a held file. A fixed delay before "nothing
arrived" passes vacuously on a slow machine and proves little on a fast one. Each test now waits for
the thing it is about, or needs no wait at all.

| Test | Was | Now |
|---|---|---|
| `AtomicFileTests.WriteAllText_WhileAReaderHoldsTheFile_LandsOnceItLetsGo` (Core) | 50 ms, then the reader lets go | Lets go once the writer has reached the held file. Its temporary file is written in full and closed, so all that is left is the move over the target. Or the write is already over, as outside Windows |
| `AtomicFileTests.WriteAllTextAsync_WhileAReaderHoldsTheFile_LandsOnceItLetsGo` (Core) | Same | Same |
| `RepositoryWatcherTests.NothingIsWatched_WhileTheSettingIsOff` | 50 ms, then "no watch" | Settles the dispatcher, where the watcher decides synchronously on the repository's event, then asserts. As a positive control, turning the setting on starts a watch of that same repository |
| `FileSystemRepositoryEventSourceTests.ADisposedWatch_ReportsNothingMore` | 300 ms, then "nothing reported" | A second, live watch of the same repository is the witness. Once it reports the change, the operating system has delivered it, and the disposed watch has still reported nothing |
| `DiffSyntaxHighlightingTests.AnUnknownExtension_StaysPlain` | 100 ms, then "plain" | No wait. No grammar means no tokenizer was started (BUG-1E83's fresh installation), so the settled editor is final. The test is synchronous again |

The temporary file counts as closed once a second handle opens it. The writer opens it unshared:
exclusive on Windows, an exclusive `flock` on Unix. The look shares deletion, so it never stands in
the way of the move.

## Files/modules touched

- Modified: `tests/Enigma.GitClient.Core.UnitTests/Configuration/AtomicFileTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryWatcherTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/FileSystemRepositoryEventSourceTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/DiffSyntaxHighlightingTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE17.md`

## Deviations & follow-ups

- **No test hook in `AtomicFile`.** The plan preferred releasing on "the first refused replace,
  observed through a test hook". Core has no `InternalsVisibleTo` (PHASE01 kept it so), and a public
  hook in a product class for one test is worse. The tests take the plan's fallback, "the writer's own
  progress". The harness shows that, on Windows, this releases with the writer already waiting on
  the held file every time (200 of 200).
- The wait for the writer polls every millisecond, bounded by the test's cancellation. That is a
  wait *for a condition*, the pattern the review recommends, not a delay standing in for one.
- Other `Task.Delay` calls in the Desktop tests are the polling of `WaitUntilAsync` / `UntilAsync`
  helpers, which wait for a condition. They are not among the review's sites.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 26 s).
  - The two `AtomicFile` tests run 21 times in a row: 21 passes.
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository** (Core only). It ran 200 held-file writes, alternating
  the sync and async writer, with the tests' wait:
  - every release came with the writer still waiting on the held file (200), never after the write was
    over (0);
  - every write landed whole, with no temporary file left (200).
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: tests only.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The held-file signal | The writer's temporary file written in full and closed, or the write over | Observable from outside, needs no product seam, and the harness shows it lands on the held path every time | An `InternalsVisibleTo` test hook (against the Core convention); a public hook; keeping the delay |
| The disposed-watch proof | A live witness watch on the same repository | Real file-system delivery has no completion signal to drive. A second watch that has seen the change is the closest thing to one | A shorter delay; asserting on the disposed watch's private watchers (a test of the implementation) |
| The unknown-extension proof | No wait: the state (no grammar) is the proof | With no grammar, nothing runs later to colour the text | Waiting a tokenizer that is never started |
