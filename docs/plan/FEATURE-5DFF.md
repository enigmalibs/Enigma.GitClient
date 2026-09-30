# FEATURE-5DFF — Release 5.0.0

**Status:** DONE — see `docs/done/FEATURE-5DFF.md`
**Type:** FEATURE
**Branch:** `feature/feature-5dff-release-5-0-0`
**Run:** feature/2026-09-30-rename-and-release

## Objective

Cut the next release once FEATURE-3988 is in, choosing its version by Semantic Versioning, and
following the previous releases (FEATURE-28C8 for 4.1.1, FEATURE-10AA for 4.0.0) as the template:

- the version;
- the release notes for everything since 4.1.1;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook; a release is a bare
  `X.Y.Z` tag on `main`; the version lives in `Directory.Build.props` (`4.1.1` today).
- **Tags:** `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0 4.0.0 4.1.0 4.1.1`. `RELEASENOTES.md`'s top section is
  4.1.1, and there is no `unreleased` section.
- **Merged into `develop` since 4.1.1:**
  - FEATURE-3071: a narrower navigation rail in both windows.
  - FEATURE-2087: a clone suggests the directory of the last clone that worked.
  - FEATURE-0842: the checked-out line is tinted light blue; *Create a tag* focuses its name box.
  - FEATURE-5261: a details panel beside the history. A line's click opens its files; the
    uncommitted line opens the working tree (stage, unstage, discard, stash, commit). **The Changes
    page is removed**, and **double-clicking a line** to open its diffs is gone.
  - FEATURE-711F: each profile has its own list of repositories, picked in the start window's header
    and ordered by dragging; a "Default" profile is made when there is none; a profile may have no
    name and email; a theme switch on the start window. **"Pin to the top" is removed**, and **the
    earlier recent-repositories list is not carried over** (`recent-repositories.json` is no longer
    read; `repository-lists.json` replaces it).
  - FEATURE-1669: a new repository's first commit is a `README.md` with its name.
  - BUG-6787: the diff's text can be selected and copied.
  - FEATURE-3988 (this run): the name "Enigma Git Client".
- **Semantic Versioning, as this project has applied it** (2.0.0, 3.0.0, 4.0.0): an upgrade that
  removes functionality or can stop an existing workflow is **MAJOR**. The Changes page, the
  double-click, "Pin to the top" and the carried-over list are all removals. So this release is
  **5.0.0**, and the notes say what moved where.
- `SECURITY.md` supports the latest minor line only (4.1.x today).
- The Avalonia set (12.1.1 → 12.1.3 available) moves only as a whole, with Enigma.Avalonia.Desktop.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 5.0.0` is empty.
2. `Directory.Build.props`: `<Version>5.0.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.0.0` section on top, in the file's shape:
   - a summary saying why this is 5.0;
   - one sub-section per area: the history's details panel; the start window's repositories; the
     diff; creating, cloning and tags; the look;
   - *Fixes*;
   - *Upgrading from 4.x*: the Changes page's work in the panel, no double-click, the list starting
     empty per profile and the old file left alone, no pin, the name;
   - *Dependencies*;
   - *Version*.
4. `README.md`: the what's-new callout names 5.0 and its highlights.
5. `SECURITY.md`: 5.0.x supported, 4.1.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 5.0.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. The Avalonia set is held back.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `5.0.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 5.0.0.
- `RELEASENOTES.md` covers everything merged since 4.1.1 and says what an upgrade from 4.x removes.
  The README callout names 5.0; `SECURITY.md` supports 5.0.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.0.0 | Functionality is removed: the Changes page, the double-click, "Pin to the top", and the recent list is not carried over. Under SemVer as 2.0/3.0/4.0 applied it, that is MAJOR | 4.2.0 (it would hide the removals); 4.1.2 (far more than fixes) |
| Dependency refresh | Checked and logged; the Avalonia set held back | Only the coupled set is outdated, and it moves as a whole with Enigma.Avalonia.Desktop, as a decision of its own | Bumping Avalonia alone |
| The README callout | "What's new in 5.0": the details panel, profile repository lists, the name, and a pointer to the upgrade notes | A reader landing on the README sees the headline changes and where the removals are explained | Listing every change |
| `SECURITY.md` | 5.0.x supported, 4.1.x unsupported | The file's own rule: the latest minor line | Supporting both lines |
| MSI profile, tag | None; bare `5.0.0`, printed | The conventions 1.0.0 to 4.1.1 set | Generating an MSI; `v5.0.0` |
