# FEATURE-A349 — Release 4.1.0

**Status:** DONE — see `docs/done/FEATURE-A349.md`
**Type:** FEATURE
**Branch:** `feature/feature-a349-release-4-1-0`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Objective

Cut the release once every other item of this run is in, numbered by Semantic Versioning. That means:

- the version;
- the release notes, with an upgrade note;
- the README callout;
- `SECURITY.md`;
- a dependency refresh;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook; a release is a bare
  `X.Y.Z` tag on `main`; the version lives in `Directory.Build.props`.
- **The last release is 4.0.0**, tagged on `main` (`git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0
  4.0.0`).
- **What this run adds:**
  - fixes — the history's column titles (BUG-29C8), dialog questions that were cut off (BUG-5349);
  - new functionality — pushing one tag from its history badge or from the Tags dialog; the *Create a
    tag* placeholder reads `1.0.0` (FEATURE-A2A2).
- **Nothing is removed or changed incompatibly**, and no stored format changes. By Semantic
  Versioning that is a **MINOR** release: **4.1.0**.
- The coupled Avalonia set stays with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 4.1.0` is empty (if it exists, the next free number is used and noted).
2. `Directory.Build.props`: `<Version>4.1.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 4.1.0` section on top, in the file's themed shape, covering every item
   of the run, with *Upgrading from 4.0* (nothing to migrate; the placeholder is only a suggestion),
   *Dependencies* and *Version*.
4. `README.md`: the *What's new in 4.1* callout; the features list where the run changed it.
5. `SECURITY.md`: 4.1.x supported, 4.0.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 4.1.0.
7. Dependency refresh (`dotnet list package --outdated`): non-coupled patch/minor bumps applied; the
   Avalonia set and majors held; every change and hold-back logged.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `4.1.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 4.1.0.
- `RELEASENOTES.md` describes every item of the run and the upgrade; the README callout names 4.1;
  `SECURITY.md` names 4.1.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 4.1.0 | A new, backward-compatible feature (pushing a tag) and fixes; nothing removed, nothing stored changes — MINOR | 4.0.1 (a PATCH carries fixes only, and this adds a feature); 5.0.0 (nothing is incompatible) |
| Dependency refresh | Non-coupled patch/minor, logged; Avalonia set and majors held | `dotnet-release`'s rule, as in every release so far | No refresh |
| MSI profile, tag | None; bare `4.1.0`, printed | The conventions 1.0.0 to 4.0.0 set | Generating an MSI; `v4.1.0` |
