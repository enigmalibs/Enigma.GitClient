# FEATURE-1795 — Release 5.6.0

**Item:** FEATURE-1795 — Release 5.6.0
**Branch:** `feature/feature-1795-release-5-6-0`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Summary

This cuts **5.6.0**, a MINOR release under Semantic Versioning. It ships everything this run's
branch carries beyond the `5.5.0` tag. All six items were done; none was quarantined:

- **BUG-6590**: a refresh no longer resets the working-tree diff (scroll, selection, widened
  context, *Show anyway*);
- **BUG-09AD**: the horizontal scrollbar no longer hides the diff's last line;
- **FEATURE-3E91**: the history's search finds commits by SHA prefix and by author too, and steps
  through its matches (`match xx/yyy`, the arrows, Enter / Shift+Enter);
- **FEATURE-0743**: roomier ref badges, and a check on the checked-out branch;
- **FEATURE-630E**: folder and terminal buttons on every row of the start window's list;
- **BUG-6EA3**: opening a folder (or a file, or a link) no longer reports a false failure on Windows.

**Why 5.6.0:** it is what you asked for, and SemVer agrees. The release adds backward-compatible
behaviour. Nothing under `src/Enigma.GitClient.Core` changed since `5.5.0`, so `settings.json` stays
at version 6.

**What changed for the release:**

- `Directory.Build.props`: `<Version>5.6.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.6.0 — 2026-10-06` section on top, in 5.5.0's shape:
  - a summary;
  - *The history*;
  - *The start window*;
  - *Fixes*;
  - *Upgrading from 5.5*;
  - *Dependencies*;
  - *Version*.
- `README.md`:
  - the callout is now *What's new in 5.6*, and it keeps the 5.1.0 rename pointer and the 4.x one;
  - the *Features* bullet on the history's search says it searches by message, SHA or author and
    steps through what it found.
- `SECURITY.md`: 5.6.x is supported; 5.5.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.6.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 0. Before tagging: run the Desktop suite where it finishes (Linux), since it was skipped this session
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Pre-flight — already run in this dev, in Release, on Windows (Core suites only; see below)
dotnet build Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main (develop matches main @ 47ed909)
git switch develop
git merge --no-ff vibe/2026-10-06-diff-search-refs-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.5.0 are
git tag 5.6.0

# 4. Push
git push origin develop main
git push origin 5.6.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 5.6.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-1795.md`: statuses

**Created**

- `docs/done/FEATURE-1795.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Sections of the notes | *The history*, *The start window*, *Fixes*, *Upgrading from 5.5* | 5.5.0's shape: one section per area that gained something, with the three bugs under *Fixes* |
| What *Upgrading from 5.5* warns about | The wider search, the count's new place, a slightly wider Refs column | They are visible changes nobody asked for in their own install. The stored data doesn't change |
| How sure the notes are about the Windows link and file cases | "could affect" | Only the folder case was reported. The others share the code path, but were not seen to fail |
| MSI profile | None, as for every release so far | The packaging is the Linux installer (`docs/RELEASE.md`) |
| Where the Release pre-flight builds | `--artifacts-path` into the session's scratch directory | `tests/Enigma.GitClient.Core.UnitTests/bin/Release` is held by a test host that hung in this session's baseline run (see below). The repository's own Release outputs could not be rebuilt in place |

## Deviations & follow-ups

- **The Desktop suite was not run**, at your request ("The desktop unit tests are broken on windows,
  they never finish. Skip them for this session"). Every Desktop test this run added or changed is
  compiled, and only BUG-6590's `DiffRefreshTests` ran (11/11, before the instruction). **Run the whole
  suite on Linux before tagging** (runbook step 0).
- **Worth a click before tagging:**
  - in the uncommitted line's diff, scroll and select text, then wait out a refresh;
  - scroll a long diff to its end and hover the horizontal bar;
  - search a short SHA, then press the arrows, Enter and Shift+Enter;
  - check the checked-out branch's badge;
  - on Windows, click the folder button on the toolbar and on a home-list row.
- **A hung test host is still running on this machine:** PID 35940,
  `tests\Enigma.GitClient.Core.UnitTests\bin\Release\net10.0\Enigma.GitClient.Core.UnitTests.exe`.
  It comes from this session's first baseline run, and it is spinning in
  `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` (BUG-6EAA). Stopping it was not
  permitted from the run, so it needs ending by hand (Task Manager, or `Stop-Process -Id 35940`).
- BUG-6EAA (the Windows test suite) stays abandoned. It is why the Desktop suite cannot finish on
  Windows and why the racing test hangs.
- The Avalonia set (12.1.1 → 12.1.3) is still held back.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout and search bullet,
`SECURITY.md`, and `docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 5.6.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`: only the Avalonia set moved, 12.1.1 →
  12.1.3: Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter and Avalonia.Themes.Fluent, plus
  Avalonia.Headless and Avalonia.Skia in the tests. It is held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --artifacts-path <scratch>`: 0 warnings, 0 errors.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.6.0+c37beb9e481f68cf4b3972f0575274220baf4285`, the run-branch commit this dev was cut from.
- `Enigma.GitClient.Core.IntegrationTests`, Release (artifacts path): 351 total, 349 passed,
  2 skipped, 0 failed.
- `Enigma.GitClient.Core.UnitTests`, Release (artifacts path): 1159 total, 1153 passed, 2 skipped,
  4 failed. All four are `DisplayNameConsistencyTests`, which find the repository by walking up from
  their output folder: *"The solution root is not reachable from the test output directory."* An
  artifacts path outside the repository has no repository above it. In the repository, in Debug, on
  this same commit:
  - `Enigma.GitClient.Core.UnitTests`: 1159 total, 1158 passed, 1 skipped, 0 failed;
  - `DisplayNameConsistencyTests`: 4/4, against this release's README and notes.
- The racing `AtomicFileTests` test is excluded from every run (BUG-6EAA, it hangs on Windows).
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your request.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
