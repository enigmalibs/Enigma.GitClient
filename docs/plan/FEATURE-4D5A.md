# FEATURE-4D5A — Release 5.2.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-4d5a-release-5-2-0`
**Run:** feature/2026-10-02-tags-branches-release

## Objective

Cut **5.2.0** once this run's other items are in, following FEATURE-0C53 (5.1.0) as the template:

- the version;
- the release notes for everything since 5.1.1;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook. A release is a bare
  `X.Y.Z` tag on `main`, and the version lives in `Directory.Build.props` (`5.1.1` today).
- **Tags:** `1.0.0` … `5.1.1`. `RELEASENOTES.md`'s top section is 5.1.1.
- **In this release (this run):**
  - FEATURE-6149: tags deleted locally and on the remote, from both tag menus;
  - FEATURE-551C: a branch and its upstream on the same commit as one badge; "reset local to here"
    when checking out a remote; a double-click on a branch badge checks it out.
- **Semantic Versioning:** new behaviour, nothing removed, nothing stored changes: **MINOR**. 5.2.0
  is what was asked, and it agrees.
- `SECURITY.md` supports the latest minor line only (5.1.x today).
- The Avalonia set moves only as a whole, with what Enigma.Avalonia.Desktop is built against
  (1.2.0 → 12.1.1).
- A run never tags, pushes or merges into `develop`/`main`. Those commands are printed.

## Steps

1. Check `git tag --list 5.2.0` is empty.
2. `Directory.Build.props`: `<Version>5.2.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.2.0` section on top, in the file's shape: a summary; one
   sub-section per area (the tags, the branch badges); *Upgrading from 5.1* (nothing to migrate);
   *Dependencies*; *Version*.
4. `README.md`: the what's-new callout names 5.2 and its highlights.
5. `SECURITY.md`: 5.2.x supported; 5.1.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 5.2.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch/minor bumps applied;
   the Avalonia set held.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `5.2.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 5.2.0.
- `RELEASENOTES.md` covers every item of the run and the upgrade. The README callout names 5.2, and
  `SECURITY.md` supports 5.2.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.2.0 | As asked. New behaviour, nothing removed, nothing stored changes: SemVer MINOR agrees | 5.1.2 (the features are new behaviour); 6.0.0 (nothing breaks) |
| What the release contains | FEATURE-6149 and FEATURE-551C | The work merged since the `5.1.1` tag | Waiting for more work |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release (FEATURE-0C53) | Moving the set |
| The README callout | "What's new in 5.2": the tag deletes, the grouped badge, reset local to here, the double-click | A reader landing on the README sees the headline changes | Listing every change |
| `SECURITY.md` | 5.2.x supported, 5.1.x unsupported | The file's own rule: the latest minor line | Supporting both lines |
| MSI profile, tag | None; bare `5.2.0`, printed | The conventions 1.0.0 to 5.1.1 set | Generating an MSI; `v5.2.0` |
| Breakdown | One dev | A release is one reviewable commit of version and documents | Splitting notes from the version |
