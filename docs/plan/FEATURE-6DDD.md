# FEATURE-6DDD — Release 3.1.0

**Status:** DONE — see `docs/done/FEATURE-6DDD.md`
**Type:** FEATURE
**Branch:** `feature/feature-6ddd-release-3-1-0`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

Cut the release once every other item of this run is in, numbered by Semantic Versioning: the version,
the release notes, the README callout, a dependency refresh, a Release pre-flight, and the
merge/tag/push runbook printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook; a release is a bare
  `X.Y.Z` tag on `main`; the version lives in `Directory.Build.props`.
- **The last release is 3.0.0**, tagged (`git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0`).
- **What this run adds:** new, backward-compatible functionality (stashes in the history, drag-to-merge
  in the history, sortable branch and tag lists, a resizable graph column, copy and select menu items,
  menu icons, a loader) and backward-compatible fixes (late badges, Azure DevOps sign-in, untracked
  file diffs). New settings properties default when absent; nothing is removed or changed
  incompatibly.
- By SemVer that is a **MINOR** release: **3.1.0**.
- The coupled Avalonia set is held with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 3.1.0` is empty (if it exists, the next free PATCH/MINOR is used and noted).
2. `Directory.Build.props`: `<Version>3.1.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 3.1.0` section on top, themed like the previous ones, covering every
   item of this run.
4. `README.md`: the *what's new* callout for 3.1.
5. `SECURITY.md`: supported line updated to 3.1.x if it names the minor line.
6. Dependency refresh (`dotnet list package --outdated`): non-coupled patch/minor bumps applied; the
   Avalonia set and majors held; every change and hold-back logged.
7. Pre-flight in Release: build clean, suite green, the Core assembly carries `3.1.0`.
8. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 3.1.0.
- `RELEASENOTES.md` describes every item of the run; the README callout names 3.1.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 3.1.0 | 3.0.0 is released; new backward-compatible features make it MINOR | 3.0.1 (features are not a patch); 4.0.0 (nothing incompatible) |
| Dependency refresh | Non-coupled patch/minor, logged; Avalonia set and majors held | `dotnet-release`'s rule, as in every release so far | No refresh |
| MSI profile, tag | None; bare `3.1.0`, printed | The conventions 1.0.0 to 3.0.0 set | Generating an MSI; `v3.1.0` |
