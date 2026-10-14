# FEATURE-1CE1 — Release 6.1.0

**Status:** DONE — see `docs/done/FEATURE-1CE1.md`
**Type:** FEATURE
**Branch:** `feature/feature-1ce1-release-6-1-0`
**Run:** feature/2026-10-14-home-filter-clear-release

## Objective

Cut **6.1.0**, with FEATURE-AE9F (6.0.0) as the template:

- the version;
- the release notes for everything `develop` and this run add beyond 6.0.0;
- the README callout, and the *Features* lines the earlier items left to the release;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.0.0` today).
- **6.0.0 is tagged.** `main` (`7027e12`) carries it. This run was cut from `develop` (`fc9f9e0`),
  which adds one merge on top: the run `feature/2026-10-14-home-search-force-push`.
- **6.1.0 carries three items:**
  - **FEATURE-8970**, on `develop`: the home list's filter box, and a drag that carries the tile and
    lands on a dashed tile;
  - **FEATURE-5BF5**, on `develop`: a refused push offers a force push, behind a red warning, with a
    lease pinned to the commit the warning named;
  - **FEATURE-68B9**, this run: the filter box's clear button. Left out of the notes if quarantined.
- **Semantic Versioning:** 6.1.0 is the number asked for, and a minor release: new,
  backward-compatible functionality, nothing removed, no default or stored format changed.
- **FEATURE-8970 and FEATURE-5BF5 left their notes to the release.** FEATURE-5BF5 already updated the
  README's *Remotes* bullet; nothing in the README names the home list's filter yet.
- `SECURITY.md` supports the latest minor line only.
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- **Tests on this machine:** the Desktop suite is compiled, not run (the project's standing
  instruction for Windows); the Core unit and integration suites are run. The runbook says to run the
  Desktop suite on Linux before tagging.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.1.0` is empty.
2. `Directory.Build.props`: `<Version>6.1.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.1.0` section on top, in 6.0.0's shape, keeping only the sections
   with something to say: a summary; one section per area (the start window's list, pushing);
   *Dependencies*; *Version*.
4. `README.md`:
   - the callout names 6.1, and keeps the *Upgrading from 5.9*, 5.1.0 rename and 4.x pointers;
   - the start-window *Features* line mentions the filter (with its clear button) and the drag.
5. `SECURITY.md`: 6.1.x is supported; 6.0.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.1.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings (AVLN included);
   - the Core unit and integration suites green; the Desktop suite compiled;
   - the assemblies carry `6.1.0`.
9. Print the runbook: this run into `develop`, `develop` into `main`, tag `6.1.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Core Release suites are green.
- The built assemblies carry 6.1.0.
- `RELEASENOTES.md` covers FEATURE-8970, FEATURE-5BF5 and every `DONE` item of this run.
- The README callout names 6.1. `SECURITY.md` supports 6.1.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.
- Running the Desktop suite on this machine.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.1.0 | As asked, and minor under SemVer: three features added, nothing removed, no default or stored format changed | 7.0.0 (nothing breaks); 6.0.1 (these are features, not fixes) |
| What 6.1.0 contains | FEATURE-8970 and FEATURE-5BF5 from `develop`, and this run's `DONE` items | Everything merged since the `6.0.0` tag; nothing else is unreleased | Only this run's item (would leave two shipped features undocumented) |
| README *Features* | Extend the start-window line with the filter, its clear button and the drag | FEATURE-8970 left that wording to the release; FEATURE-5BF5's *Remotes* line is already current | A new bullet |
| MSI profile, tag | None; bare `6.1.0`, printed | The convention every release so far has set | An MSI; `v6.1.0` |
| Desktop suite | Compiled here; the runbook asks for a Linux run before tagging | The project's standing instruction for this Windows machine | Running it here |
| Integration path in the runbook | The run branch into `develop`, `develop` into `main`, tag `6.1.0` | The house flow: every release reaches `main` through `develop` | Merging the run branch straight into `main` |
| Breakdown | One item, one dev | A routine release is one reviewable commit (dotnet-release) | — |
