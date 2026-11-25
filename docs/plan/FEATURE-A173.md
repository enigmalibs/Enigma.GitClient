# FEATURE-A173 — Release 6.20.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-a173-release-6-20-0`
**Run:** feature/2026-11-25-release-6-20-0

## Objective

Cut **6.20.0**, with FEATURE-9C3D (6.19.0) as the template, following the `dotnet-release` skill.
The release covers:

- the version;
- the release notes for what 6.20.0 adds beyond 6.19.0;
- the README callout, and any README line the release's changes left stale;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight, all three suites.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.19.0` today). `buildmsi.ps1` reads it from
  there and has no version of its own.
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.20.0 (z is 0).
- **6.20.0 carries** what was built since the `6.19.0` tag (`git log 6.19.0..HEAD`), all `DONE`:
  CODE-REVIEW-6E56, the fixes for the 2026-11-19 solution review (`docs/review/2026-11-19-solution.md`),
  27 phases, PHASE05 abandoned. The user-facing ones:
  - SEC-01 (PHASE01): system programs and git started by their full path, never a bare name;
  - SEC-02 (PHASE02): the token store's files and the accounts list replaced in one step;
  - EXC-01 (PHASE03): an unreadable accounts file is moved aside, not overwritten;
  - SEC-03 (PHASE06): a changed file that would run is shown in the file manager, never opened;
  - LOG-01 (PHASE13): a rolling log file, named by the *Something went wrong* dialog;
  - PERF-03 (PHASE14): the create and clone dialogs check the disk off the UI thread;
  - PERF-01 (PHASE15) and PERF-02 (PHASE16): lists refilled in one go; long agreed text folded on the
    conflict page;
  - XAML-03 (PHASE20): the head badge's text, the avatars' initials and tints follow the theme;
  - STYLE-01 (PHASE21): a command given nothing does nothing.

  The rest (PHASE04, 07–12, 17–19, 22–28) changes no behaviour: structure, tests, styles.
- **The starting point** is `develop` @ `ee420c7`, which merged the review run.
- **README:** PHASE13 already added the `logs/` row and the `appsettings.json` paragraph to *Where your
  things are kept*. The release checks them; it does not rewrite them.
- **Stored formats:** no `CurrentVersion` moved since `6.19.0`. `AppSettings` 7, `RepositoryListStore`
  1, `HostAccountService` 2, `TokenStore` 1, `IdentityProfileStore` 1, `HiddenBranchStore` 1 — checked
  against the files at build time.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only with Enigma.Avalonia.Desktop. In 6.19.0 it was
  held at 12.1.4, with Enigma.Avalonia.Desktop 1.3.0.
- **This machine is Linux.** The Desktop suite runs here: it is where the 6.19.0 release asked it to
  run before tagging. CODE-REVIEW-6E56's Desktop tests were only ever compiled (on Windows), so this is
  their first run.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1`: it starts
  with `git clean -dxf`.

## Steps

1. Check that `git tag --list 6.20.0` is empty.
2. `Directory.Build.props`: `<Version>6.20.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.20.0 — <date>` section on top, in 6.19.0's shape:
   - a summary list;
   - one section per user-facing change: programs by their full path; files that would run; the
     accounts and tokens; the log file; the create and clone dialogs; conflicts; the light theme;
   - *Under the hood*: the review's structural changes in a few lines;
   - *Upgrading from 6.19*: the MSI and `install.sh`, the stored formats and going back, the new
     `logs/` folder, and what now behaves differently (a git found only through a relative `PATH`
     entry is no longer used; a terminal that is not installed is not tried; *Open file* gone for a
     file that would run);
   - *Dependencies*, *Version*.

   Every path, file name, menu and dialog text, limit and number the notes quote is checked against
   the files.
4. `README.md`:
   - the callout names 6.20 and sums up 6.19 after it. 6.18's sentence goes. The older upgrade notes
     stay;
   - *Features* and *Where your things are kept* checked against the review's changes, fixed only
     where they are wrong.
5. `SECURITY.md`: 6.20.x supported; 6.19.x no longer.
6. `docs/RELEASE.md`: any line naming the current version is brought to 6.20.0. History stays.
7. Dependency check (`dotnet list Enigma.GitClient.slnx package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release, on Linux:
   - a clean build (`--no-incremental`) with zero warnings, the Debug build too;
   - the Core unit, Core integration and Desktop suites against the run's baseline, with no new
     failure;
   - the built Core and Desktop assemblies carry `6.20.0`;
   - `buildmsi.ps1`'s version lookup, replayed read-only, gives `Enigma.GitClient.6.20.0.msi`.

## Acceptance criteria

- The Release and Debug builds are clean, with zero warnings.
- The Core unit, Core integration and Desktop suites show no failure the baseline lacks. Any failure
  the baseline has is named in the done doc and the run's report.
- The built assemblies carry 6.20.0, and the version-reading tests pass (`ProductInformationTests`,
  `HostHttpTests`, `DisplayNameConsistencyTests`, `AboutDialogTests`).
- `RELEASENOTES.md` covers CODE-REVIEW-6E56's user-facing changes, with *Upgrading from 6.19* naming
  the behaviour changes. Its quoted paths, texts and numbers match the files.
- The README callout names 6.20.
- `SECURITY.md` supports 6.20.x only.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or
  installs an MSI.

## Out of scope

- NuGet packaging, building the MSI, and moving the Avalonia set on its own.
- Fixing a test the baseline already fails: that is a BUG item of its own.
- Reopening CODE-REVIEW-6E56-PHASE05 / BUG-6EAA (the Desktop suite on Windows).
- Merging into `develop`/`main` and tagging: the user does these after the run.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.20.0 | The user's rule, x.y+1.z with z = 0. The review's fixes add behaviour (the log file, the fold) and break nothing: a minor release | 6.19.1 (the log file and the conflict fold are features); 7.0.0 (nothing breaks) |
| What it carries | CODE-REVIEW-6E56, and the review report it fixes | Exactly the commits since the `6.19.0` tag | Waiting for more items |
| Starting point | The current `HEAD`, `develop` @ `ee420c7` | The run rule is to cut from `HEAD`, and it holds the review run | — |
| Shape of the notes | 6.19.0's, plus a short *Under the hood* section | House shape. Most of the review's 27 phases are structural: a list of them would bury the few a user meets | One section per phase; leaving the structural work out entirely |
| README callout | 6.20, then 6.19; 6.18's sentence goes | House shape: the newest two releases, then the older upgrade notes | Keeping 6.18's sentence |
| Desktop suite | Run, in Release, here on Linux, in the baseline and the pre-flight | The 6.19.0 release left "run it on Linux before tagging" as a follow-up, and CODE-REVIEW-6E56's Desktop tests have never run | Compiled only, as on Windows (the gap that let BUG-FACC through) |
| A Desktop failure already in the baseline | Recorded, flagged as a gate before tagging, not fixed in this dev | A release dev changes no product code; a fix needs its own item and review | Fixing it inside the release (scope creep, unreviewed code in a release commit) |
| Container-validation harness | Dropped | It stood in for `CompositionRootTests`, which runs here | Keeping it (duplicate of a real test) |
| Dependencies | Non-coupled patch and minor bumps applied. The Avalonia set held unless Enigma.Avalonia.Desktop moved | House rule from earlier releases | Bumping everything |
| Pre-flight configuration | Release, with the baseline in Release too. The Debug build checked for warnings | `docs/RELEASE.md` pre-flights in Release, and a baseline compares only in the same configuration | Debug only |
| Installer | No edit to `buildmsi`. Its version lookup is replayed read-only | It reads `Directory.Build.props` | Hard-coding the version; running the script (`git clean -dxf`) |
| Phasing | One phase | A routine release (`dotnet-release`) | Splitting the docs from the version |
