# FEATURE-AE9F — Release 6.0.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-ae9f-release-6-0-0`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Objective

Cut **6.0.0**, with FEATURE-158D (5.9.0) as the template:

- the version;
- the release notes for everything this run adds beyond 5.9.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`5.9.0` today).
- **5.9.0 is tagged and published.** `main` (`9f3d7c6`) and `develop` (`20f8f77`) carry the same tree.
  This run was cut from `develop`.
- **6.0.0 carries this run's three items:**
  - **FEATURE-B313**, the window title;
  - **FEATURE-532B**, the file-system watcher, the *Watch the repository for changes* setting, and the
    60 s refresh default (settings schema 7);
  - **FEATURE-8E87**, the compact Branches, Tags and Remotes pages with multi-select and bulk delete.

  Any of them quarantined is left out of the notes.
- **Semantic Versioning:** 6.0.0 is the number asked for, and it is a major release by the
  precedent of 5.0.0. It removes functionality:
  - the per-row buttons of the three pages (check out, delete, the eye, fetch, edit, remove);
  - their second lines;
  - the "upstream gone" and "published" markers.

  It also changes a default (the refresh interval).
- **`settings.json` moves to version 7.** 5.9 still reads it: it keeps the keys it knows and ignores
  `WatchFileSystem`.
- `SECURITY.md` supports the latest minor line only.
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.0.0` is empty.
2. `Directory.Build.props`: `<Version>6.0.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.0.0` section on top, in 5.x's shape, keeping only the sections
   with something to say:
   - a summary;
   - *New features*;
   - *Changes* (what was removed or moved);
   - *Upgrading from 5.9*;
   - *Dependencies*;
   - *Version*.
4. `README.md`: the callout names 6.0, and keeps the 5.1.0 rename and 4.x pointers.
5. `SECURITY.md`: 6.0.x is supported; 5.9.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.0.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings;
   - all three suites green (Core unit, Core integration, Desktop, all runnable on Linux);
   - the assemblies carry `6.0.0`.
9. Print the runbook: this run into `develop`, `develop` into `main`, tag `6.0.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings, and all three Release suites are green.
- The built assemblies carry 6.0.0.
- `RELEASENOTES.md` covers every `DONE` item of the run and the upgrade from 5.9.
- The README callout names 6.0. `SECURITY.md` supports 6.0.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.0.0 | As asked, and major by the 5.0.0 precedent: the three pages' per-row actions and second lines are removed, and a default changes | 5.10.0 (the number asked for wins when it is SemVer-valid) |
| What 6.0.0 contains | This run's `DONE` items | The run is cut from 5.9.0's tree; nothing else is unreleased | — |
| Settings in the notes | *Upgrading from 5.9*: schema 7, the 15 → 60 move, and going back to 5.9 | The one stored-data change a user must know about | Leaving it to the README |
| MSI profile, tag | None; bare `6.0.0`, printed | The convention every release so far has set | An MSI; `v6.0.0` |
| Integration path in the runbook | The run branch into `develop`, `develop` into `main`, tag `6.0.0` | The house flow: every release reaches `main` through `develop` | Merging the run branch straight into `main` |
| Breakdown | One item, one dev | A routine release is one reviewable commit (dotnet-release) | — |
