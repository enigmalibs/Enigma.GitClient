# FEATURE-1B37 — Release 6.2.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-1b37-release-6-2-0`
**Run:** feature/2026-10-14-release-6-2-0

## Objective

Cut **6.2.0**, with FEATURE-1CE1 (6.1.0) as the template:

- the version;
- the release notes for what `develop` adds beyond 6.1.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.1.0` today).
- **6.1.0 is tagged.** `main` (`77ed980`) carries it. This run was cut from `develop` (`88d5014`),
  which adds one merge on top: the run `feature/2026-10-14-app-icon-connections`.
- **6.2.0 carries one item: FEATURE-E228**, on `develop`. The application icon's green commit joins
  the line of commits by one rounded elbow out of the base commit, instead of a horizontal stub from
  the middle commit and a diagonal back. Tile, colours and dots are kept; the blue dots sit on an
  even pitch. The icon gained SVG masters under `packaging/icon/`. FEATURE-E228 left its notes to the
  release.
- **Where the icon shows:** the window chrome, the taskbar, the splash screen and the About dialog
  (`Assets/app.ico`, `Assets/app-256.png`), and the Linux launcher (the six hicolor PNGs that
  `install.sh` installs, then refreshes the icon cache for).
- **Semantic Versioning:** 6.2.0 is the number asked for, and a minor release: a visible,
  backward-compatible change, nothing removed, no default or stored format changed
  (`git diff 6.1.0 develop` touches nothing under `Core/Configuration`).
- `SECURITY.md` supports the latest minor line only.
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- **Tests on this machine:** Linux, so every suite runs — Core unit, Core integration and Desktop.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.2.0` is empty.
2. `Directory.Build.props`: `<Version>6.2.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.2.0` section on top, in 5.9.0's one-change minor shape:
   a summary; *The icon*; *Upgrading from 6.1*; *Dependencies*; *Version*.
4. `README.md`: the callout names 6.2, sums up 6.1 after it, points a 5.9 user at *Upgrading from
   5.9* under 6.0.0, and keeps the 5.1.0 rename and 4.x pointers.
5. `SECURITY.md`: 6.2.x is supported; 6.1.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.2.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings (AVLN included);
   - the Core unit, Core integration and Desktop suites green;
   - the assemblies carry `6.2.0`.
9. Print the runbook: this run into `develop`, `develop` into `main`, tag `6.2.0`, push, publish.

## Acceptance criteria

- The Release build is clean with zero warnings, and every Release suite is green.
- The built assemblies carry 6.2.0.
- `RELEASENOTES.md` covers FEATURE-E228.
- The README callout names 6.2. `SECURITY.md` supports 6.2.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.
- The icon itself (FEATURE-E228 is done), and its optional follow-ups (PNG recompression, a
  `hicolor/scalable` install).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.2.0 | As asked, and minor under SemVer: a visible, backward-compatible change, nothing removed, no default or stored format changed | 7.0.0 (nothing breaks); 6.1.1 (it is not a fix) |
| What 6.2.0 contains | FEATURE-E228 | Everything merged since the `6.1.0` tag (`git log 6.1.0..develop`); nothing else is unreleased | — |
| The notes' shape | 5.9.0's one-change minor shape | The latest release that carried a single change; 6.1.0's per-area shape is for several | 6.1.0's shape with one area |
| README callout | 6.2 first, then *6.1 before it*; 6.0 reduced to a *Coming from 5.9?* pointer; the 5.1.0 and 4.x pointers kept | 5.9.0's shape: one previous release summed up. 6.0's *Upgrading from 5.9* is still what a 5.9 user must read | Keeping the full 6.0 summary (the callout grows each release) |
| README *Features* | Untouched | No feature line describes the icon; the install table's "six sizes" is still true | A feature line for the icon |
| MSI profile, tag | None; bare `6.2.0`, printed | The convention every release so far has set | An MSI; `v6.2.0` |
| Tests | Every suite, here | This machine is Linux; nothing has to be deferred | Compiling the Desktop suite only |
| Integration path in the runbook | The run branch into `develop`, `develop` into `main`, tag `6.2.0` | The house flow: every release reaches `main` through `develop` | Merging the run branch straight into `main` |
| Breakdown | One item, one dev | A routine release is one reviewable commit (dotnet-release) | — |
