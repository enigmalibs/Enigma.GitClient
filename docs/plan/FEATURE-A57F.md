# FEATURE-A57F — Release 5.8.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-a57f-release-5-8-0`
**Run:** vibe/2026-10-08-file-panel-about-release

## Objective

Cut **5.8.0** once every other item of this run is done or quarantined, with FEATURE-47B3 (5.7.0) as
the template:

- the version;
- the release notes for what this run adds beyond 5.7.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`5.7.0` today).
- **5.7.0 is tagged** (`5.7.0`), and `main` (`1146b20`) carries it merged from `develop`. This run
  started from `main` @ `1146b20`.
- **In this release:** BUG-1B14, BUG-D0D9, FEATURE-E2D2 and FEATURE-62B7. Any of them quarantined is
  left out of the notes, and the completion doc says so.
- **Semantic Versioning:** new features (the expand/collapse buttons), so a minor version, 5.8.0.
- **No stored-data change expected:** `settings.json` stays at its schema (to verify).
- `SECURITY.md` supports the latest minor line only (5.7.x today).
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 5.8.0` is empty.
2. `Directory.Build.props`: `<Version>5.8.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.8.0` section on top, in 5.7.0's shape, keeping only the sections
   that have something to say: summary, the areas changed, *Fixes*, *Upgrading from 5.7*,
   *Dependencies*, *Version*.
4. `README.md`: the what's-new callout names 5.8 and keeps the older pointers.
5. `SECURITY.md`: 5.8.x is supported; 5.7.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 5.8.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch/minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: the build is clean, the suites are green (the same Windows caveats as the
   run's other devs), and the assemblies carry `5.8.0`.
9. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release Core suites are green.
- The built assemblies carry 5.8.0.
- `RELEASENOTES.md` covers every done item of this run, and the upgrade from 5.7.
- The README callout names 5.8.
- `SECURITY.md` supports 5.8.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.8.0 | As asked; new features make it a minor release | 5.7.1 (a patch cannot carry features) |
| What the release contains | Every done item of this run | It is on the run branch and not in 5.7.0 | Waiting for quarantined items |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release | Moving the set |
| `SECURITY.md` | 5.8.x supported, 5.7.x unsupported | The file's own rule | Supporting both lines |
| MSI profile, tag | None; bare `5.8.0`, printed | The convention every release so far has set | An MSI; `v5.8.0` |
| Breakdown | One item, one dev, last in the run | A release is one reviewable commit, and it must describe what the run built | Splitting the notes from the version |
| Integration path in the runbook | Run branch → `develop` → `main`, then tag | `docs/RELEASE.md`'s flow; `develop` is at `d39d1a0`, already in `main` | Merging straight into `main` |
