# FEATURE-2408 — Release 3.0.0 with the refresh fix

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-2408-release-3-0-0`
**Run:** bugfix/2026-09-27-remote-refresh-release

## Objective

Cut the release once `BUG-6B9E` is in, numbered by Semantic Versioning. That means:

- the release notes cover the fix;
- the version is checked;
- a dependency refresh;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook; a release is a bare
  `X.Y.Z` tag on `main`.
- **3.0.0 is prepared but not released.** `FEATURE-5CD8` set `<Version>3.0.0</Version>` and wrote the
  3.0.0 notes. The run was merged into `develop`, which is pushed. But there is no `3.0.0` tag, and
  `main` is still at 2.0.0.
- **The version, by SemVer:**
  - The last release is 2.0.0.
  - Since then: `FEATURE-6C81`, which is incompatible (why 3.0.0 is a major release — see
    `FEATURE-5CD8`), and `BUG-6B9E`, a backward-compatible fix.
  - A version number names a release. An untagged version's contents can still change; only a
    released one's must not.
  - So the release is still **3.0.0**, and the fix joins its notes.
  - Should a `3.0.0` tag exist when this is built, 3.0.0 is released: the fix is then a PATCH,
    **3.0.1**, with a section of its own and the version bumped.
- The coupled Avalonia set is held with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. `git tag --list 3.0.0`:
   - none → 3.0.0: `Directory.Build.props` stays at `3.0.0`;
   - found → 3.0.1: `<Version>3.0.1</Version>`, a new `## 3.0.1 — <date>` section, and `SECURITY.md`
     at 3.0.x (unchanged).
2. `RELEASENOTES.md` — in the 3.0.0 section (or the 3.0.1 one), a *The history* sub-section, placed
   before *Upgrading from 2.0*:
   - the history follows remote branches and tags on its own;
   - at once after the toolbar's fetch, pull or push;
   - otherwise at the next automatic refresh, every 15 seconds by default.
3. Dependency refresh:
   - run `dotnet list package --outdated`;
   - apply the non-coupled patch/minor bumps;
   - hold the Avalonia set (and anything major) back;
   - log every change and every hold-back.
4. The README callout and `SECURITY.md` are left as they are for 3.0.0, and checked.
5. Pre-flight in Release: build clean, suite green, and the Core assembly carries the version.
6. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release suite is green.
- The built assemblies carry the release's version.
- `RELEASENOTES.md` describes `BUG-6B9E` in the release's section. The dependency sub-section
  reflects this refresh.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 3.0.0, with the fix folded in, unless a `3.0.0` tag exists at build time (then 3.0.1) | SemVer numbers releases, and 3.0.0 has none: no tag, not on `main`. Relative to 2.0.0, the release is still a MAJOR one, and a fix does not change that | 3.0.1 now (it would tag a 3.0.0 known to leave the history stale, only to replace it at once); 3.1.0 (a fix is not a feature) |
| Where the fix is described | A *The history* sub-section of the release's section | The file's themed shape | A *Fixes* list |
| README callout | Unchanged | It names the release's headline, the sign-in; the fix is not one | Adding the fix to it |
| Dependency refresh | Non-coupled patch/minor bumps, logged; the Avalonia set and majors held | `dotnet-release`'s rule, as in every release so far | No refresh |
| MSI profile, tag | None; bare `3.0.0`, printed | The conventions 1.0.0 to 3.0.0 set | Generating an MSI; `v3.0.0` |
