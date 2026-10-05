# FEATURE-4707 — Release 5.4.0

**Status:** DONE — see `docs/done/FEATURE-4707.md`
**Type:** FEATURE
**Branch:** `feature/feature-4707-release-5-4-0`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Objective

Cut **5.4.0** once this run's other items are in, following FEATURE-3858 (5.3.0) as the template:

- the version;
- the release notes for everything since 5.3.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook. A release is a bare
  `X.Y.Z` tag on `main`; the version lives in `Directory.Build.props` (`5.3.0` today).
- **In this release (this run):** FEATURE-659E, FEATURE-1CCB, FEATURE-1E0D — whichever of their devs are
  merged (a quarantined one is left out of the notes).
- **Semantic Versioning:** new behaviour, nothing removed: **MINOR**. 5.4.0 is what was asked, and it
  agrees. The settings file moves to schema 6 (FEATURE-1CCB) — compatible: an older build reads it for
  the keys it knows.
- `SECURITY.md` supports the latest minor line only (5.3.x today).
- The Avalonia set moves only as a whole, with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`. Those commands are printed.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.4.0 | As asked; new behaviour, nothing removed: SemVer MINOR agrees | 5.3.1 (new behaviour); 6.0.0 (nothing breaks) |
| The tree coming back | Called out under *Upgrading from 5.3* | A 5.x install that never chose a shape opens on a tree again — a visible change nobody asked of their own install | Burying it in the feature list |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release | Moving the set |
| `SECURITY.md` | 5.4.x supported, 5.3.x unsupported | The file's own rule | Supporting both lines |
| MSI profile, tag | None; bare `5.4.0`, printed | The conventions 1.0.0 to 5.3.0 set | Generating an MSI; `v5.4.0` |
| Breakdown | One dev, last in the run | A release is one reviewable commit of version and documents | Splitting notes from the version |

## Steps

1. Check `git tag --list 5.4.0` is empty.
2. `Directory.Build.props`: `<Version>5.4.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.4.0` section on top, in the file's shape: a summary; one sub-section
   per area; *Upgrading from 5.3*; *Dependencies*; *Version*.
4. `README.md`: the what's-new callout names 5.4 and its highlights.
5. `SECURITY.md`: 5.4.x supported; 5.3.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 5.4.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch/minor bumps applied;
   the Avalonia set held.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `5.4.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 5.4.0.
- `RELEASENOTES.md` covers every merged item of the run and the upgrade; the README callout names 5.4;
  `SECURITY.md` supports 5.4.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.
