# FEATURE-1CE1 — Release 6.1.0

**Item:** FEATURE-1CE1 — Release 6.1.0
**Branch:** `feature/feature-1ce1-release-6-1-0`
**Run:** feature/2026-10-14-home-filter-clear-release

## Summary

This cuts **6.1.0**, with FEATURE-AE9F (6.0.0) as the template. The run was cut from `develop`
(`fc9f9e0`), which is 6.0.0 (`main`, `7027e12`) plus one merged run. 6.1.0 brings three items, all
`DONE`:

- **FEATURE-8970** (`develop`): the start window's filter box, and a drag that carries the whole tile
  and lands on a dashed tile.
- **FEATURE-5BF5** (`develop`): a refused push offers a force push, behind a red warning, with a lease
  pinned to the commit the warning named.
- **FEATURE-68B9** (this run): the filter box's clear button.

**Why 6.1.0:** it is the number you asked for, and it is minor under Semantic Versioning. The release
adds backward-compatible features, removes nothing, and changes no default or stored format
(`settings.json` stays at version 7; `git diff 7027e12` touches nothing under `Core/Configuration`).

**What changed for the release:**

- `Directory.Build.props`: `<Version>6.1.0</Version>`.
- `Directory.Packages.props`: four patch bumps (see *Dependencies* below).
- `RELEASENOTES.md`: a dated `## 6.1.0 — 2026-10-14` section on top, in 5.8.0's minor-release shape:
  - a summary;
  - *The start window's list*: the filter, its clear button, no reordering while filtered, the drag;
  - *Pushing*: the force-push offer, the pinned lease, nothing offered over unfetched commits;
  - *Upgrading from 6.0*;
  - *Dependencies*;
  - *Version*.
- `README.md`:
  - the callout is now *What's new in 6.1*, with 6.0 summed up after it (5.9.0's shape), keeping the
    *Upgrading from 5.9*, 5.1.0 rename and 4.x pointers;
  - the start-window *Features* line names the filter, its ×, and the drag's tile.
- `SECURITY.md`: 6.1.x is supported; 6.0.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 6.1.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — the build and the Core suites already ran in this dev, in Release, on Windows.
#    The Desktop suite was compiled, not run: run it on Linux before tagging.
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-14-home-filter-clear-release
git switch main
git merge --no-ff develop

# 3. Tag 6.1.0 — bare X.Y.Z, as 1.0.0 to 6.0.0 are
git tag 6.1.0

# 4. Push
git push origin develop main
git push origin 6.1.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 6.1.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `Directory.Packages.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-1CE1.md`: statuses

**Created**

- `docs/done/FEATURE-1CE1.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' shape | 5.8.0's minor-release shape: a bulleted summary, one section per area, then *Upgrading*, *Dependencies*, *Version* | The latest minor release with new features; 6.0.0's longer shape is a major release's |
| The order of the areas | The start window's list first, then pushing | Two of the three items are the list; the clear button belongs with the filter it clears |
| *Upgrading from 6.0* | Kept, one line: nothing saved changes | Every 5.x minor release says so; a reader coming from 6.0 looks there first |
| The README callout | 6.1 first, then *6.0 before it*, keeping the older pointers | 5.9.0's callout shape for a minor release that follows another; 6.0's *Upgrading from 5.9* is still the one a 5.9 user must read |
| The Microsoft.Extensions bumps | All three 10.0.12 → 10.0.13 together | One servicing release of one runtime set; the props file pins them side by side |
| xunit.v3 4.0.1 → 4.0.2 | Applied | A patch in the test projects only; the Core suites ran green on it |
| The Avalonia set | Held back at 12.1.1 | 12.1.4 is out, but Enigma.Avalonia.Desktop 1.2.0 has no update and pins 12.1.1; the set moves as a whole |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |

## Deviations & follow-ups

- **None from the plan.**
- **The Desktop suite was not run on this machine** (the project's standing instruction for Windows).
  It compiled in Release with the new xunit.v3 and the four FEATURE-68B9 tests. Run it on Linux
  before tagging; 6.0.0's two known Desktop flakes (`AutoRefreshTests`,
  `RepositoriesPageTests.OpeningARepository_…`) may show there.
- **Run on Linux before tagging**, when the run was committed (see *Build/test evidence*). The first
  Desktop run hit both known races:
  - `RepositoriesPageTests.OpeningAListedRepositoryAgain_LeavesItWhereItIs` failed in its teardown
    with *Directory not empty: …/first/.git*, the race 6.0.0 recorded for its sibling test;
  - `AutoRefreshTests.TheRefreshButton_PicksUpACommitMadeInATerminal` found no row for the new
    commit.

  The next two full runs passed. Neither test is touched by this release.
- **Carried over:**
  - the Avalonia set (12.1.1 → 12.1.4) is still held back;
  - the other filter boxes have no in-box clear button (FEATURE-68B9);
  - Settings does not say when the watcher has fallen back (FEATURE-532B);
  - a remote branch delete asks no push guard (FEATURE-8E87).
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout and *Features* line,
`SECURITY.md` and `docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 6.1.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - Microsoft.Extensions.Hosting, Microsoft.Extensions.Http and
    System.Security.Cryptography.ProtectedData: 10.0.12 → 10.0.13, applied;
  - xunit.v3: 4.0.1 → 4.0.2, applied;
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, and
    Avalonia.Headless and Avalonia.Skia in the tests): 12.1.1 → 12.1.4, held back.
  - The restored assets resolve the four new versions.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**, no AVLN
  warning.
- `Enigma.GitClient.Core.UnitTests` (Release): **1183 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **361 passed, 2 skipped, 0 failed** (4 min 17 s).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** on Windows (see *Deviations &
  follow-ups*).
- **Re-run on Linux** when the run was committed: `dotnet build -c Release --no-incremental`
  **0 warnings, 0 errors**, no AVLN warning; `Enigma.GitClient.Desktop.UnitTests` **1529 passed,
  0 failed** on the second and third runs (2 known flakes on the first);
  `Enigma.GitClient.Core.UnitTests` **1183 passed, 1 skipped**; `Enigma.GitClient.Core.IntegrationTests`
  **363 passed**.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.1.0+da48362a582fe2c651a4bfac4cc27c41e797525b`: the run branch's tip this dev's changes sit on.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
