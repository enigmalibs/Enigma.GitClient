# FEATURE-47B3 — Release 5.7.0

**Item:** FEATURE-47B3 — Release 5.7.0
**Branch:** `feature/feature-47b3-release-5-7-0`
**Run:** vibe/2026-10-06-stash-untracked-release

## Summary

This cuts **5.7.0**. It ships what this run's branch carries beyond 5.6.0, which is one item, done
and not quarantined:

- **BUG-1F3A**: a stash's line in the history lists every file the stash holds, including the
  untracked files it took. An untracked file shows as added, and opens with every line added.

**Why 5.7.0:** it is what you asked for. A minor release may carry a fix only; SemVer's minimum would
have been 5.6.1. The only source change since 5.6.0 is in `DiffService`, `DiffTarget` and the
history page. `settings.json` stays at version 6.

**5.6.0 is not tagged.** The newest tag is `5.5.0`, and `main` is still at it (`47ed909`). 5.6.0's
release commit is on `develop` (`4d6dfd4`, merged as `5ea9463`). Its notes are left as they are, and
5.7.0's section sits on top. Tagging 5.6.0 is optional, step 3a of the runbook.

**What changed for the release:**

- `Directory.Build.props`: `<Version>5.7.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.7.0 — 2026-10-06` section on top, in 5.6.0's shape, keeping only
  the sections with something to say:
  - a summary;
  - *Fixes*;
  - *Upgrading from 5.6*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is now *What's new in 5.7*. It keeps a line on 5.6, the 5.1.0 rename
  pointer and the 4.x one. Its last lines are re-wrapped to the file's width.
- `SECURITY.md`: 5.7.x is supported; 5.6.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.7.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 0. Before tagging: run the Desktop suite where it finishes (Linux); it was not run this session
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Pre-flight — already run in this dev, in Release, on Windows (Core suites only; see below)
dotnet build Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff vibe/2026-10-06-stash-untracked-release
git switch main
git merge --no-ff develop

# 3a. Optional — 5.6.0 was never tagged; tag its release commit on develop if you want it to exist
git tag 5.6.0 5ea9463

# 3b. Tag 5.7.0 — bare X.Y.Z, as 1.0.0 to 5.5.0 are
git tag 5.7.0

# 4. Push
git push origin develop main
git push origin 5.7.0          # and 5.6.0, if you tagged it

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 5.7.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-47B3.md`: statuses

**Created**

- `docs/done/FEATURE-47B3.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Sections of the notes | *Fixes*, *Upgrading from 5.6*, *Dependencies*, *Version* | 5.6.0's shape, without the area sections a single fix has nothing for |
| What *Upgrading from 5.6* says | Settings unchanged; older stashes show their untracked files too | The only visible change, and it applies to stashes made before the upgrade |
| The README callout | 5.7 first, then a line on 5.6 | 5.6.0 is not tagged, so someone upgrading from 5.5 gets both releases at once |
| The Release pre-flight | Built in place (`-c Release`) | No test host was holding the Release outputs this time, unlike in the 5.6.0 run |
| A flaky integration test in the first Release run | Rerun once, then recorded; not fixed here | See *Deviations & follow-ups*. It is an old race in a test this release does not touch, and fixing it is outside a release dev |

## Deviations & follow-ups

- **A flaky test, not fixed (follow-up).**
  `RepositoryServiceTests.CloneAsync_ClonesALocalRepositoryAndReportsProgress` failed once in the
  first full Release run, then passed 5/5 alone and in the full rerun.
  - The cause is in the test. It collects reports through `Progress<T>`, which posts each callback
    to the thread pool, then asserts at once that the list is non-empty and starts with `Starting`.
    Under load, the callbacks may not have run yet. They also add to a `List<T>` from several
    threads.
  - The test's own comment says the reports "may still be arriving".
  - Suggested fix, in its own item: a synchronous `IProgress<T>` (an inline class) in the test.
- **The Desktop suite was not run, and no Desktop test was added**, as you asked. Run it on Linux
  before tagging (runbook step 0).
- **Worth a click before tagging:** stash a tracked change and two untracked files from the toolbar,
  then select the stash line. Check that all three are listed and that an untracked one opens with
  every line added.
- The Avalonia set (12.1.1 → 12.1.3) is still held back.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 5.7.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - only the Avalonia set moved, 12.1.1 → 12.1.3: Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter
    and Avalonia.Themes.Fluent, plus Avalonia.Headless and Avalonia.Skia in the tests;
  - it is held back with Enigma.Avalonia.Desktop 1.2.0;
  - no other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.7.0+e0b37bcda3557d74c011fadfd69c2d64c2db39f0`, the run-branch commit this dev was cut from.
- `Enigma.GitClient.Core.UnitTests`, Release: 1165 total, 1164 passed, 1 skipped, 0 failed.
  `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` was excluded, because it hangs on
  Windows (BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`, Release:
  - first run: 358 total, 1 failed (the clone-progress race above), 2 skipped;
  - that test alone: 5/5 passed;
  - full rerun: 358 total, 356 passed, 2 skipped, 0 failed.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, as you asked.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 1 cycle counted, conservatively. It was the full rerun; no code changed.
