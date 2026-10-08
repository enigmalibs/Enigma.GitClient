# FEATURE-2414-PHASE01 — No git auto-maintenance in tests

**Item:** FEATURE-2414 — Faster integration test suite, PHASE01
**Branch:** `feature/feature-2414-phase01-no-auto-maintenance`
**Run:** vibe/2026-10-08-slow-hanging-tests

## Summary

The integration tests' throwaway repositories no longer start git's automatic maintenance.

- git starts a `git maintenance run --auto --detach` after every commit, merge, fetch and push. In
  the baseline run, that was **920 of the suite's 7,054 git starts (13%)**.
- `GitWorkspace` now writes `maintenance.auto = false` and `gc.auto = 0` into the workspace's isolated
  system config. The fixture's git and the product's git both read that file.
- A traced run now starts **6,134** git processes, **none of them maintenance**.

## Files / modules touched

**Modified**

- `tests/Enigma.GitClient.Core.IntegrationTests/Infrastructure/GitWorkspace.cs`: the system config's
  two settings, with a comment saying why they are in that file and not the global one.
- `docs/roadmap.md`, `docs/plan/FEATURE-2414.md`: statuses.

**Created**

- `docs/done/FEATURE-2414-PHASE01.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How to measure honestly on a noisy machine | Count git starts with `GIT_TRACE2`; time an interleaved A/B, switching maintenance back on through `GIT_CONFIG_*` env vars; and time one class run alone | The start count is deterministic, and interleaving cancels drift in machine load. A class run alone has no parallel contention |
| Keep the phase although the full-suite gain is not measurable | Yes | It deterministically removes 920 processes and makes a class run alone about 5% faster. It also stops background maintenance from racing the workspace cleanup. It costs one line of config |

## Deviations & follow-ups

- **Deviation: the full-suite wall time is not shown to be lower.** The plan's criterion, "lower than
  the 3m45s baseline", cannot be shown on this machine.
  - This afternoon the same suite took 5m03s to 6m12s with the same settings, against 3m45s this
    morning. The machine's load varied, and the Enigma Git Client app was open.
  - The interleaved A/B was flat:

    | Maintenance | Run 1 | Run 2 | Mean |
    |---|---|---|---|
    | on | 5m03s | 6m12s | 5m38s |
    | off | 6m00s | 5m34s | 5m47s |

  - Run alone, without contention, `RefReaderTests` took 48.1 s and 46.0 s with maintenance on, and
    44.2 s and 45.1 s with it off: **about 5% faster**.
  - Traced, the full suite went from 5m00s to 4m20s, but the trace slows every start and so magnifies
    the effect.
- **The assumption behind the plan was too simple.** The plan assumed wall time is (git starts ×
  ~40 ms). Measured: total CPU stays at about 30% during a run, so the suite is not CPU-bound. The run
  is held up by its **longest collections**.
  - In the baseline, `CommitLogReaderTests` started 56 s in and finished last.
  - `RefReaderTests` (178 s) finished seconds before it.
  - Both rebuild the `HistoryFixture` history for every test, which is exactly what PHASE02 removes. Its
    benefit should therefore show in those collections' times.
- **Follow-up, not acted on:** the git maintenance spawns turned out cheaper than the .NET-started
  ones, probably because git starts them itself without .NET's pipe setup. This explains why 13% of
  the starts bought only about 5% of the time.
- **CRLF:** nothing to report. The file is LF, as `.gitattributes` asks.

## Documentation sweep

Nothing stale: no human-facing doc describes the integration tests' git configuration.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1169 total, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 0 failed, 2 skipped, in every one of 6 full
  runs on this branch: 1 traced, 1 plain, and the 4 A/B runs (2 with maintenance switched back on).
- `GIT_TRACE2`: 6,134 git starts, 0 `maintenance`, 0 `gc`; before, 7,054 starts with 920 `maintenance`.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your standing instruction.
- Fix budget: 0 cycles.
