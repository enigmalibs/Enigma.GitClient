# FEATURE-10AA — Release 4.0.0

**Status:** DONE — see `docs/done/FEATURE-10AA.md`
**Type:** FEATURE
**Branch:** `feature/feature-10aa-release-4-0-0`
**Run:** bugfix/2026-09-28-changes-commit-details-release

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
- **The last release is 3.1.0**, tagged (`git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0`).
- **What this run adds:**
  - fixes — the start page's empty state, the file lines' menus;
  - new functionality — About on the start page, the Changes page's way back and Esc, blue back
    buttons, red discards with a plain question, the commit details dialog and its menu item, and
    discarding the uncommitted work from the history.
- **It also removes functionality:** the Changes page's *Amend* and *Sign off*. A 3.1 user who amended
  or signed off in the app can no longer, and has to use another tool. `FEATURE-4AC8` and
  `FEATURE-5CD8` ruled that an upgrade which can stop a user's existing workflow is a **MAJOR**
  release, so this is **4.0.0**.
- No stored format changes (`settings.json` and the rest are as 3.1 wrote them). That goes in the
  upgrade note, not the version.
- The coupled Avalonia set stays with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 4.0.0` is empty (if it exists, the next free number is used and noted).
2. `Directory.Build.props`: `<Version>4.0.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 4.0.0` section on top, in the file's themed shape, covering every item
   of the run, with *Upgrading from 3.x* (Amend and Sign off are gone; how to amend or sign off with
   git; nothing to migrate), *Dependencies* and *Version*.
4. `README.md`: the *What's new in 4.0* callout; the features list where the run changed it.
5. `SECURITY.md`: 4.0.x supported, 3.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 4.0.0.
7. Dependency refresh (`dotnet list package --outdated`): non-coupled patch/minor bumps applied; the
   Avalonia set and majors held; every change and hold-back logged.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `4.0.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 4.0.0.
- `RELEASENOTES.md` describes every item of the run and the upgrade; the README callout names 4.0;
  `SECURITY.md` names 4.0.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 4.0.0 | Amend and Sign off are removed: an upgrade that stops an existing workflow is MAJOR, as ruled for 2.0.0 and 3.0.0 | 3.2.0 (the new features alone would be MINOR, but a removal is not backward compatible) |
| Dependency refresh | Non-coupled patch/minor, logged; Avalonia set and majors held | `dotnet-release`'s rule, as in every release so far | No refresh |
| MSI profile, tag | None; bare `4.0.0`, printed | The conventions 1.0.0 to 3.1.0 set | Generating an MSI; `v4.0.0` |
