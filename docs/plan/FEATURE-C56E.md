# FEATURE-C56E — Release 6.4.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-c56e-release-6-4-0`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Objective

Cut **6.4.0**, with FEATURE-6352 (6.3.0) as the template, following the `dotnet-release` skill:

- the version;
- the release notes for what 6.4.0 adds beyond 6.3.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on `main`.
  The version lives in `Directory.Build.props` (`6.3.0` today).
- **6.4.0 carries two items**, both `DONE` by the time this dev starts:
  - **FEATURE-C3F3** — removing worktrees (with or without their branch), the *Worktrees* tool dialog,
    the tree mark on branches checked out in another working tree, the repository strip's worktree hint,
    and the up-front refusals of deleting or checking out such a branch;
  - **FEATURE-CA9D** — the windows open on the screen in use: the hand-over between the start and
    repository windows, a fresh start on the last screen, another instance on its caller's screen.
  If either is not `DONE`, stop and ask.
- **Semantic Versioning:** a minor release — new, backward-compatible behaviour. `settings.json` stays at
  version 7: `LastScreen` is a remembered value, added without a schema bump. Nothing is removed.
- `SECURITY.md` supports the latest minor line only.
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- **Tests on this machine:** Windows. The Core unit and integration suites run; the Desktop suite is
  compiled, **not run** (standing instruction) — the runbook says to run it on Linux before tagging, and
  to do FEATURE-CA9D's two-screen manual check.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.4.0` is empty.
2. `Directory.Build.props`: `<Version>6.4.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.4.0` section on top, in 6.3.0's several-change minor shape: a summary
   list; *Worktrees* (remove, the dialog, the marks, the refusals); *Windows and screens*; *Upgrading
   from 6.3*; *Dependencies*; *Version*.
4. `README.md`: the callout names 6.4, sums up 6.3 after it (6.2's summary goes), and keeps the older
   pointers 6.3.0 kept.
5. `SECURITY.md`: 6.4.x is supported; 6.3.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.4.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged: non-coupled patch and minor bumps
   applied; the Avalonia set held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: a clean build with zero warnings (AVLN included); the Core suites green; the
   Desktop suite compiled; the assemblies carry `6.4.0`.
9. Print the runbook: the branch into `develop`, `develop` into `main`, tag `6.4.0`, push, publish — with
   the Desktop suite run on Linux and the two-screen check first.

## Acceptance criteria

- The Release build is clean with zero warnings; the Core suites are green; the Desktop suite compiles.
- The built assemblies carry 6.4.0.
- `RELEASENOTES.md` covers FEATURE-C3F3 and FEATURE-CA9D. The README callout names 6.4. `SECURITY.md`
  supports 6.4.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.

## Decisions

| Question | Chosen | Source |
|---|---|---|
| Plan a release | Yes, 6.4.0, after both features | User |
| Version number | 6.4.0, minor: new backward-compatible behaviour, no stored format changed | Default (SemVer) |
| The notes' shape, callout, MSI, tag | As 6.3.0: several-change minor shape; bare tag, printed | House convention (FEATURE-6352) |
| Tests | Core suites run, Desktop compiled; Linux run and two-screen check in the runbook | Standing instruction |
| Breakdown | One item, one dev, after FEATURE-CA9D | Default |
