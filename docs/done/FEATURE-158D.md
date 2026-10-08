# FEATURE-158D — Release 5.9.0

**Item:** FEATURE-158D — Release 5.9.0
**Branch:** `feature/feature-158d-release-5-9-0`
**Run:** vibe/2026-10-08-release-5-9-0

## Summary

This cuts **5.9.0**. `develop`, where the run started, carried nothing beyond 5.8.0, so the release
first merges the finished, unmerged run `vibe/2026-10-08-slow-hanging-tests` (tip `d27aa66`) into this
branch. That run brings:

- **BUG-3163**, a product fix. On Windows, `AtomicFile` lost a write when another instance had the
  file open for reading. That file can be `settings.json`, the identity profiles, the recent
  repositories or the hidden branches. The final replace is now retried while the file is held open:
  up to 50 attempts, 10 ms apart.
- **FEATURE-2414**, tests only:
  - the integration tests' repositories no longer start git's automatic maintenance;
  - the history fixture is built once per run and copied;
  - the integration suite is about 18% faster where it was measured.

**Why 5.9.0:** it is the number you asked for. A minor bump is allowed for a fix and internal
improvements, and 5.7.0 was a fix-only minor release too. The only `src` change since 5.8.0 is
`AtomicFile.cs`, and `settings.json` stays at version 6 (`AppSettings.CurrentVersion`).

**What changed for the release:**

- The merge commit `40b5990`, `Merge branch 'vibe/2026-10-08-slow-hanging-tests'`. Its one conflict,
  in `docs/roadmap.md`, resolved to BUG-3163 and FEATURE-2414 (with their phases) above
  FEATURE-158D, keeping that run's longer BUG-6EAA footnote.
- `Directory.Build.props`: `<Version>5.9.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.9.0 — 2026-10-08` section on top, in 5.7.0's and 5.8.0's shape:
  - a summary;
  - *Fixes*;
  - *The test suites*;
  - *Upgrading from 5.8*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is now *What's new in 5.9*. It keeps a "5.8 before it" line, because 5.8.0
  is not published yet, and keeps the 5.1.0 rename and 4.x pointers.
- `SECURITY.md`: 5.9.x is supported; 5.8.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.9.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 0. Before tagging: run the Desktop suite where it works (Linux); it was not run this session
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Pre-flight — already run in this dev, in Release, on Windows (Core suites only; see below)
dotnet build Enigma.GitClient.slnx -c Release

# 2. Finish 5.8.0 first: develop (69c87e5) carries it, but it is not tagged and main is at 5.7.0
git switch main
git merge --no-ff develop
git tag 5.8.0

# 3. Integrate 5.9.0: the run branch into develop, then develop into main
git switch develop
git merge --no-ff vibe/2026-10-08-release-5-9-0
git switch main
git merge --no-ff develop

# 4. Tag 5.9.0 — bare X.Y.Z, as 1.0.0 to 5.7.0 are
git tag 5.9.0

# 5. Push
git push origin develop main
git push origin 5.8.0 5.9.0

# 6. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 5.9.0`.

## Files / modules touched

**Brought in by the merge** (from `vibe/2026-10-08-slow-hanging-tests`, unchanged)

- `src/Enigma.GitClient.Core/Configuration/AtomicFile.cs`
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/AtomicFileTests.cs`
- `tests/Enigma.GitClient.Core.IntegrationTests/`:
  - `Infrastructure/GitWorkspace.cs`, `Infrastructure/HistoryFixture.cs`;
  - `Infrastructure/HistoryTemplate.cs` (new), `HistoryTemplateTests.cs` (new);
  - `History/CommitLogReaderTests.cs`, `Refs/RefReaderTests.cs`.
- `docs/plan/BUG-3163.md`, `docs/plan/FEATURE-2414.md`
- `docs/done/BUG-3163.md`, `docs/done/FEATURE-2414-PHASE01.md`, `docs/done/FEATURE-2414-PHASE02.md`
- `docs/roadmap.md`: the conflict resolution

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-158D.md`: statuses

**Created**

- `docs/done/FEATURE-158D.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' fix entry | Phrased for users: what was lost, when, and that Linux never was | The notes are read by users; `AtomicFile` and the retry budget are in BUG-3163's records |
| The retry's duration in the notes | "Half a second or so" | 49 waits of 10 ms is at least 0.5 s, and Windows' coarse sleep makes it longer. "Up to" would understate it |
| *Upgrading from 5.8* | The settings line only | Nothing to do and nothing changes in behaviour beyond the fix |
| The README *Features* list | Unchanged | No feature changed; the fix and the test suites are not features |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |
| The Release pre-flight | Built in place, `-c Release --no-incremental` | No test host was holding the Release outputs |

## Deviations & follow-ups

- **Deviation from the starting point, as planned:** this release carries a run you had not merged
  into `develop` yet, `vibe/2026-10-08-slow-hanging-tests`. Review it as part of this run
  (`git log --oneline develop..vibe/2026-10-08-slow-hanging-tests` lists its seven commits). If you do not want it in 5.9.0,
  this release has nothing else to carry.
- **5.8.0 is still untagged.** The runbook tags it first, on `develop` as it is today (`69c87e5`),
  so the *Upgrading from 5.8* note and `SECURITY.md`'s 5.8.x row describe a real release.
- **The Desktop suite was not run** (your standing instruction). Nothing in this release touches
  Desktop code, but run it on Linux before tagging (runbook step 0).
- Carried over:
  - the Avalonia set (12.1.1 → 12.1.3) is still held back;
  - the Desktop teardown on Windows (BUG-6EAA's other half) is still open;
  - `%TEMP%` may still hold `enigma-atomic-*` leftovers from runs before BUG-3163.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. The merged run's own sweep found nothing stale. No `CLAUDE.md` or `AGENTS.md`
exists.

## Build/test evidence

- `git tag --list 5.9.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - the Avalonia set has 12.1.1 → 12.1.3 for Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter and
    Avalonia.Themes.Fluent, plus Avalonia.Headless and Avalonia.Skia in the tests. It is held back
    with Enigma.Avalonia.Desktop 1.2.0, which has no update;
  - nothing else moved, so nothing was bumped.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.9.0+40b59907f5e3791850fb9a38804a462314a133e1`, the merge commit this dev's changes sit on.
- `Enigma.GitClient.Core.UnitTests`, Release: 1169 total, 0 failed, 1 skipped. The racing test is
  included. Run alone (`--filter-method *AReaderRacingTheWriter_AlwaysReadsAWholeDocument`), it
  passed in 5 s.
- `Enigma.GitClient.Core.IntegrationTests`, Release: 360 total, 0 failed, 2 skipped, in 4m39s. The
  two `HistoryTemplateTests` are among the 360.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your standing instruction.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
