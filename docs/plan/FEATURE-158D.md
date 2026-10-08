# FEATURE-158D — Release 5.9.0

**Status:** DONE — see `docs/done/FEATURE-158D.md`
**Type:** FEATURE
**Branch:** `feature/feature-158d-release-5-9-0`
**Run:** vibe/2026-10-08-release-5-9-0

## Objective

Cut **5.9.0**, with FEATURE-A57F (5.8.0) as the template:

- the version;
- the release notes for what 5.9.0 adds beyond 5.8.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`5.8.0` today).
- **5.8.0 is not tagged yet.** This run started from `develop` @ `69c87e5`, which carries the 5.8.0
  release merged in (FEATURE-A57F). `main` (`1146b20`) is still at 5.7.0, and `git tag` stops at
  `5.7.0`.
- **`develop` carries nothing beyond 5.8.0.** The one finished piece of work not released yet is the
  run `vibe/2026-10-08-slow-hanging-tests` (tip `d27aa66`, cut from `69c87e5`, every item `DONE`, not
  merged anywhere). 5.9.0 carries it:
  - **BUG-3163**, a product fix. On Windows, `AtomicFile` lost a write when another instance had the
    file open for reading. That file can be `settings.json`, the identity profiles, the recent
    repositories or the hidden branches.
  - **FEATURE-2414**, tests only:
    - the integration tests' repositories no longer start git's automatic maintenance;
    - the history fixture is built once and copied;
    - the integration suite is about 18% faster.
- **Semantic Versioning:** one fix and internal improvements. 5.9.0 is the number asked for, and a
  minor bump is allowed for them; 5.7.0 set the same precedent for a fix-only minor release.
- **No stored-data change expected:** `settings.json` stays at version 6 (to verify).
- `SECURITY.md` supports the latest minor line only (5.8.x today).
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.
- The Desktop suite is not run on this machine (standing instruction); it is compiled only.

## Steps

1. Check that `git tag --list 5.9.0` is empty.
2. On the dev branch, `git merge --no-ff vibe/2026-10-08-slow-hanging-tests`. The roadmap conflict
   resolves to BUG-3163 and FEATURE-2414 (with their phases) above FEATURE-158D, and to the longer
   BUG-6EAA footnote, which mentions BUG-3163.
3. `Directory.Build.props`: `<Version>5.9.0</Version>`.
4. `RELEASENOTES.md`: a dated `## 5.9.0` section on top, in 5.8.0's and 5.7.0's shape, keeping only
   the sections that have something to say: summary, *Fixes*, *The test suites*, *Upgrading from
   5.8*, *Dependencies*, *Version*.
5. `README.md`: the what's-new callout names 5.9. It keeps a "5.8 before it" line, because 5.8.0 is not
   published yet, and keeps the 5.1.0 rename and 4.x pointers.
6. `SECURITY.md`: 5.9.x is supported; 5.8.x no longer is.
7. `docs/RELEASE.md`: the MSI paragraph lists 5.9.0 among the releases without a profile.
8. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch/minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
9. Pre-flight in Release:
   - the build is clean;
   - the Core unit and integration suites are green;
   - the Desktop suite is compiled, not run;
   - the assemblies carry `5.9.0`.
10. Print the runbook. It first finishes 5.8.0 (merge `develop` into `main`, tag `5.8.0`), then
    integrates this run and tags `5.9.0`.

## Acceptance criteria

- The release branch carries every commit of `vibe/2026-10-08-slow-hanging-tests`.
- The Release build is clean with zero warnings, and the Release Core suites are green, with
  `AReaderRacingTheWriter_AlwaysReadsAWholeDocument` included.
- The built assemblies carry 5.9.0.
- `RELEASENOTES.md` covers BUG-3163, FEATURE-2414 and the upgrade from 5.8.
- The README callout names 5.9.
- `SECURITY.md` supports 5.9.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging 5.8.0 or 5.9.0, merging into `develop`/`main`, pushing.
- The Desktop suite on Windows (BUG-6EAA's other half).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What 5.9.0 contains | 5.8.0 plus the finished run `vibe/2026-10-08-slow-hanging-tests`, merged `--no-ff` into this dev | `develop` has nothing beyond 5.8.0. That run is complete, green, and holds the only unreleased product fix (BUG-3163) | Releasing `develop` as is (5.9.0 would be 5.8.0 with another number); stopping (not allowed after pre-flight) |
| Where that merge happens | On the release dev branch, before its own commit | The planning commit stays the run's first commit, and only dev branches are merged into the run branch. If the release were quarantined, the merge would be left out with it | On the run branch before planning; on the run branch between planning and the dev |
| Version number | 5.9.0 | As asked. A minor bump is allowed for internal improvements, and 5.7.0 was a fix-only minor release | 5.8.1 (the number asked for wins when it is SemVer-valid) |
| Test-only work in the notes | A short *The test suites* section | It ships in the tag, and the release pre-flight runs those suites | Leaving it out of the notes |
| README callout | 5.9 first, with a "5.8 before it" line | 5.8.0 is not published, so someone coming from 5.7 gets both, as the 5.7 callout did for 5.6 | Dropping the 5.8 line |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release | Moving the set |
| `SECURITY.md` | 5.9.x supported, 5.8.x unsupported | The file's own rule: the latest minor line only | Supporting both lines |
| MSI profile, tag | None; bare `5.9.0`, printed | The convention every release so far has set | An MSI; `v5.9.0` |
| Integration path in the runbook | Finish 5.8.0 first (`develop` into `main`, tag `5.8.0`), then this run into `develop`, `develop` into `main`, tag `5.9.0` | Every version gets tagged, and *Upgrading from 5.8* stays true | Skipping the 5.8.0 tag |
| Breakdown | One item, one dev | A routine release is one reviewable commit (dotnet-release) | Splitting the merge from the notes |
