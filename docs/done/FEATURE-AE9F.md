# FEATURE-AE9F — Release 6.0.0

**Item:** FEATURE-AE9F — Release 6.0.0
**Branch:** `feature/feature-ae9f-release-6-0-0`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

This cuts **6.0.0**, with FEATURE-158D (5.9.0) as the template. The run was cut from `develop`
(`20f8f77`), which carries the same tree as 5.9.0 on `main`. 6.0.0 brings this run's three items,
all `DONE`:

- **FEATURE-B313:** the window title says *Enigma Git Client*.
- **FEATURE-532B:** the file-system watcher, the *Watch the repository for changes* setting, and the
  60 s refresh default (settings schema 7).
- **FEATURE-8E87:** the compact Branches, Tags and Remotes pages, the branch tree, multi-select and
  bulk delete.

**Why 6.0.0:** it is the number you asked for, and it is major by the 5.0.0 precedent. The release
removes functionality: the three pages' row buttons, their second lines, and the *upstream gone* and
*published* markers. It also changes a default, the refresh interval, and migrates it.

**What changed for the release:**

- `Directory.Build.props`: `<Version>6.0.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.0.0 — 2026-10-08` section on top, in 5.0.0's shape:
  - a summary;
  - *The repository is watched*;
  - *The automatic refresh*;
  - *Branches, tags and remotes*;
  - *Selecting and deleting several*;
  - *The window's title*;
  - *Changes*: what was removed or moved;
  - *Upgrading from 5.9*: the menus, settings schema 7 and the 15 → 60 move, going back to 5.9, and
    the inotify limits;
  - *Dependencies*;
  - *Version*.
- `README.md`:
  - the callout is now *What's new in 6.0*, keeping the 5.1.0 rename and 4.x pointers;
  - two *Features* lines: the pages drawn like the changed files with the branch tree, and deleting
    several at once.
- `SECURITY.md`: 6.0.x is supported; 5.9.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 6.0.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release, on Linux (see Build/test evidence)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-08-title-watcher-refs-release
git switch main
git merge --no-ff develop

# 3. Tag 6.0.0 — bare X.Y.Z, as 1.0.0 to 5.9.0 are
git tag 6.0.0

# 4. Push
git push origin develop main
git push origin 6.0.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 6.0.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-AE9F.md`: statuses

**Created**

- `docs/done/FEATURE-AE9F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' order | What is new first (the watcher, the refresh, the pages, the bulk delete, the title), then *Changes* and *Upgrading from 5.9* | 5.0.0's shape: a major release says what it gives before what it takes |
| The removed buttons in the notes | Listed per page in *Changes*, with where each action went | A reader looking for the eye or *Fetch* finds where it moved |
| Going back to 5.9 | Said plainly: 5.9 reads version 7, keeps the interval, and drops `watchFileSystem` when it saves a preference | `SettingsService` reads a newer file for the keys it knows (checked in 5.9.0's source) |
| The inotify limits | A line in *Upgrading from 5.9*, with the two `sysctl` names | The one machine-level change a Linux user may need; PHASE03 measured 3 instances per repository |
| README *Features* | Two lines added, for the pages and the bulk delete | PHASE02 and PHASE04 left that wording to the release; the watcher's line came with FEATURE-532B |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |
| The Avalonia set | Held back at 12.1.1 | 12.1.3 is out, but Enigma.Avalonia.Desktop 1.2.0 has no update; the set moves as a whole |

## Deviations & follow-ups

- **Two Desktop tests fail now and then, in Release and under the full suite's load only.** Neither
  is a product failure. Run alone, their classes passed every time, in 25 and 30 runs. The
  starting point was measured with 12 full Desktop runs of its tree, exported with
  `git archive 20f8f77`.
  - **`AutoRefreshTests.TheNextRefresh_RedrawsAPushMadeBetweenTwoRefreshes`** found no history row
    for a commit the repository had all along.
    - It failed in 1 of 16 runs here, and in 1 of the 12 at the starting point: it predates this
      run.
    - The likely mechanism: two history reloads overlap. `RefreshInPlaceAsync` is guarded only by
      `IsBusy`, which is false while it waits for the working tree. The superseded reload still
      raises `RowsReplaced` while the other one has the rows emptied.
    - Follow-up: raise `RowsReplaced` only for the load that finished, or let one in-place refresh
      run at a time.
  - **`RepositoriesPageTests.OpeningARepository_PublishesItRemembersItAndShowsTheHistory`** failed
    in its teardown: *Directory not empty: …/my-repo/.git*.
    - It failed in 2 of 16 runs here, and in none of the 12 at the starting point.
    - Its cause is older than this run. Opening a repository starts `RefreshNowAsync` without
      waiting for it (`RepositoryOpener`, FEATURE-426F). Its `git fetch --all` writes
      `.git/FETCH_HEAD` even with no remote, while `TestServices.Dispose` deletes the folder.
    - Whether this run made the race likelier is not settled by 12 runs.
    - Follow-up: the test waits for that refresh, or the teardown retries the delete.
- **Tried and taken out (fix cycle 1):** turning git's automatic maintenance off for the Desktop
  test process (`maintenance.auto=false`, `gc.auto=0`, through `GIT_CONFIG_*`).
  - A commit does fork `git maintenance run --auto --detach` here.
  - The teardown failure came back with the switch on, so maintenance was not its cause.
  - The change was removed rather than shipped for the wrong reason.
- **Not changed:** both flakes are test races in code this release does not touch. Fixing them is a
  work item of its own.
- **Carried over:**
  - the Avalonia set (12.1.1 → 12.1.3) is still held back;
  - Settings does not say when the watcher has fallen back (FEATURE-532B);
  - a remote branch delete asks no push guard (FEATURE-8E87).
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout and two *Features* lines,
`SECURITY.md` and `docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 6.0.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - The Avalonia set has updates, 12.1.1 → 12.1.3: Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter
    and Avalonia.Themes.Fluent, plus Avalonia.Headless and Avalonia.Skia in the tests.
  - The set is held back with Enigma.Avalonia.Desktop 1.2.0, which has no update.
  - Nothing else moved, so nothing was bumped.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`, on the tree this commit holds:
  - 3028 total, 3027 passed, 1 skipped, 0 failed;
  - the Core unit, Core integration and Desktop suites all passed.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.0.0+105f7c584b38be4b0252a24838e557145a9b55b8`: the run branch's tip this dev's changes sit on.
- Fix budget: 1 cycle (above).
