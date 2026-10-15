# FEATURE-6352 — Release 6.3.0

**Status:** DONE — see `docs/done/FEATURE-6352.md`
**Type:** FEATURE
**Branch:** `feature/feature-6352-release-6-3-0`
**Run:** feature/2026-10-15-create-identity-release

## Objective

Cut **6.3.0**, with FEATURE-1B37 (6.2.0) and FEATURE-1CE1 (6.1.0) as the templates:

- the version;
- the release notes for what this run branch adds beyond 6.2.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.2.0` today).
- **6.2.0 is tagged** (`main`, `dd8aa1f`). `develop` (`d0962e4`) carries it. This run was cut from
  `feature/2026-10-15-worktrees-toolbar` (`aed2a17`), not yet merged into `develop`.
- **6.3.0 carries three items**, all `DONE` on this run branch by the time this dev starts:
  - **FEATURE-70F3**: the repository window's toolbar no longer has the *Open another window* button;
    the start window's *Open in a new window, beside this one* stays;
  - **FEATURE-19A5**: *Create worktree…* on a commit's line, a branch's badge and a branch's line in
    the branches dialog — a new branch checked out in a folder of its own, next to the repository,
    opened in a new window;
  - **FEATURE-B979**: *Create a repository* offers the global identity, as a clone does.
  FEATURE-70F3 and FEATURE-19A5 left their notes to this release.
- **Semantic Versioning:** 6.3.0 is the number asked for, and a minor release: new, backward-compatible
  behaviour; nothing stored changes (`settings.json` stays at version 7). One toolbar button is
  removed — its function stays on the start window — which the notes say under *Upgrading from 6.2*.
- `SECURITY.md` supports the latest minor line only.
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- **Tests on this machine:** Windows. The Core unit and Core integration suites run; the Desktop suite
  is compiled, **not run** (standing instruction) — the runbook says to run it on Linux before tagging.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.3.0` is empty.
2. `Directory.Build.props`: `<Version>6.3.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.3.0` section on top, in 6.1.0's several-change minor shape:
   a summary list; *Worktrees*; *The new repository's identity*; *The toolbar*; *Upgrading from 6.2*;
   *Dependencies*; *Version*.
4. `README.md`: the callout names 6.3, sums up 6.2 after it (6.1's summary goes), and keeps the 5.9,
   5.1.0 and 4.x pointers.
5. `SECURITY.md`: 6.3.x is supported; 6.2.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.3.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings (AVLN included);
   - the Core unit and Core integration suites green; the Desktop suite compiled;
   - the assemblies carry `6.3.0`.
9. Print the runbook: this run branch into `develop`, `develop` into `main`, tag `6.3.0`, push, publish
   — with the Desktop suite run on Linux first.

## Acceptance criteria

- The Release build is clean with zero warnings; the Core suites are green; the Desktop suite compiles.
- The built assemblies carry 6.3.0.
- `RELEASENOTES.md` covers FEATURE-70F3, FEATURE-19A5 and FEATURE-B979.
- The README callout names 6.3. `SECURITY.md` supports 6.3.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.
- Listing or removing worktrees (FEATURE-19A5's follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.3.0 | As asked, and minor under SemVer: new backward-compatible behaviour, no stored format changed | 7.0.0 (the removed button's function stays elsewhere); 6.2.1 (not a fix) |
| What 6.3.0 contains | FEATURE-70F3, FEATURE-19A5, FEATURE-B979 | Everything on this run branch beyond the `6.2.0` content (`develop`) | Leaving FEATURE-B979 for a later release |
| The notes' shape | 6.1.0's several-change minor shape | Three changes; 6.2.0's one-change shape is for one | One section per item ID |
| README callout | 6.3 first, then *6.2 before it*; 6.1's summary dropped; the 5.9, 5.1.0 and 4.x pointers kept | 6.2.0's shape: one previous release summed up | Keeping 6.1's summary (the callout grows each release) |
| MSI profile, tag | None; bare `6.3.0`, printed | The convention every release so far has set | An MSI; `v6.3.0` |
| Tests | Core suites run, Desktop compiled; Desktop on Linux before tagging, in the runbook | Standing instruction for this Windows machine | Running the Desktop suite here |
| Integration path in the runbook | This run branch into `develop` (it carries `feature/2026-10-15-worktrees-toolbar`), `develop` into `main`, tag `6.3.0` | The house flow: every release reaches `main` through `develop` | Merging the two run branches separately |
| Breakdown | One item, one dev, after FEATURE-B979 | A routine release is one reviewable commit (dotnet-release), and it must carry FEATURE-B979 | — |
