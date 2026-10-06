# FEATURE-1795 — Release 5.6.0

**Status:** DONE — see `docs/done/FEATURE-1795.md`
**Type:** FEATURE
**Branch:** `feature/feature-1795-release-5-6-0`
**Run:** vibe/2026-10-06-diff-search-refs-release

## Objective

Cut **5.6.0** once every other item of this run is done, with FEATURE-6A0E (5.5.0) as the template:

- the version;
- the release notes for everything since the `5.5.0` tag;
- the README callout, and the *Features* bullets this run made stale;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook; a release is a bare `X.Y.Z` tag on
  `main`; the version lives in `Directory.Build.props` (`5.5.0` today).
- **In this release:** what this run's branch carries beyond the `5.5.0` tag (`main @ 47ed909`, which
  `develop` matches):
  - BUG-6590 — a refresh no longer resets the working-tree diff;
  - BUG-09AD — the scrollbar no longer hides the diff's last line;
  - FEATURE-3E91 — the history search finds SHAs and authors, and steps through its matches;
  - FEATURE-0743 — roomier ref badges, and a check on the checked-out branch;
  - FEATURE-630E — folder and terminal buttons on the home list;
  - BUG-6EA3 — opening a folder (or a link) no longer falsely fails on Windows.

  Any of them quarantined by the run is left out of the notes and named in the completion doc.
- **No stored-data change expected:** `settings.json` stays at schema 6 (to verify).
- **Semantic Versioning:** new backward-compatible behaviour → **MINOR**; 5.6.0 is what was asked, and
  it agrees.
- `SECURITY.md` supports the latest minor line only (5.5.x today).
- The Avalonia set moves only as a whole, with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 5.6.0` is empty.
2. `Directory.Build.props`: `<Version>5.6.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.6.0` section on top, in 5.5.0's shape (summary, one sub-section
   per area, *Fixes*, *Upgrading from 5.5*, *Dependencies*, *Version*).
4. `README.md`: the what's-new callout names 5.6 and its highlights (keeping the older pointers); the
   *Features* bullets the run made stale (history search) say what is now true.
5. `SECURITY.md`: 5.6.x supported; 5.5.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph lists 5.6.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged: non-coupled patch/minor bumps applied;
   the Avalonia set held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: build clean, suite green, assemblies carry `5.6.0`.
9. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release suite is green.
- The built assemblies carry 5.6.0.
- `RELEASENOTES.md` covers every item of this run that is done, and the upgrade from 5.5.
- The README callout names 5.6.
- `SECURITY.md` supports 5.6.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.6.0 | As asked; new behaviour with no stored-data break, so SemVer MINOR agrees | 5.5.1 (new behaviour); 6.0.0 (nothing breaks) |
| What the release contains | Every item of this run that is done | All of it is on the run branch and none of it is in `5.5.0` | Waiting for quarantined items |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release | Moving the set |
| `SECURITY.md` | 5.6.x supported, 5.5.x unsupported | The file's own rule | Supporting both lines |
| MSI profile, tag | None; bare `5.6.0`, printed | The convention every release so far set | An MSI; `v5.6.0` |
| Breakdown | One item, one dev, last in the run | A release is one reviewable commit; it must describe what the run built | Splitting the notes from the version |
| Integration path in the runbook | Run branch → `develop` → `main`, then tag | `develop` matches `main @ 47ed909`, the run's start | Merging straight into `main` |
