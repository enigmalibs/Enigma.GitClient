# FEATURE-47B3 — Release 5.7.0

**Status:** DONE — see `docs/done/FEATURE-47B3.md`
**Type:** FEATURE
**Branch:** `feature/feature-47b3-release-5-7-0`
**Run:** vibe/2026-10-06-stash-untracked-release

## Objective

Cut **5.7.0** once BUG-1F3A is done, with FEATURE-1795 (5.6.0) as the template:

- the version;
- the release notes for what this run adds beyond 5.6.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for you.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`5.6.0` today).
- **5.6.0 is written but not tagged.** `develop` carries the 5.6.0 release commit (`4d6dfd4`, merged
  as `5ea9463`). `main` and the newest tag are still `5.5.0`. The `## 5.6.0` notes stay as they are;
  5.7.0 goes on top of them.
- **In this release:** BUG-1F3A, a stash line lists its untracked files. If it is quarantined, the
  release is still cut, and the completion doc says it has nothing new.
- **No stored-data change expected:** `settings.json` stays at schema 6 (to verify).
- **Semantic Versioning:** a fix alone would allow 5.6.1. 5.7.0 is what you asked for, and a minor
  release may carry fixes only.
- `SECURITY.md` supports the latest minor line only (5.6.x today).
- The Avalonia set moves only as a whole, together with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 5.7.0` is empty.
2. `Directory.Build.props`: `<Version>5.7.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.7.0` section on top, in 5.6.0's shape. It keeps only the sections
   that have something to say: summary, *Fixes*, *Upgrading from 5.6*, *Dependencies*, *Version*.
4. `README.md`: the what's-new callout names 5.7 and the fix, and keeps the older pointers.
5. `SECURITY.md`: 5.7.x is supported; 5.6.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 5.7.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged:
   - non-coupled patch/minor bumps are applied;
   - the Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: the build is clean, the Core suites are green, and the assemblies carry
   `5.7.0`.
9. Print the runbook, including the optional tag of the untagged 5.6.0.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release Core suites are green.
- The built assemblies carry 5.7.0.
- `RELEASENOTES.md` covers every done item of this run, and the upgrade from 5.6.
- The README callout names 5.7.
- `SECURITY.md` supports 5.7.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing; tagging 5.6.0.
- Running the Desktop unit-test suite (your instruction for this session).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.7.0 | As asked; a minor may carry fixes only | 5.6.1 (SemVer's minimum for a fix, but not what was asked) |
| The untagged 5.6.0 | Notes kept; tagging it at its commit is offered as an optional runbook step | Don't rewrite a written release; tagging is yours | Folding 5.6.0 into 5.7.0's notes; tagging it from the run |
| What the release contains | Every done item of this run | It is on the run branch and not in 5.6.0 | Waiting for quarantined items |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule for a minor release | Moving the set |
| `SECURITY.md` | 5.7.x supported, 5.6.x unsupported | The file's own rule | Supporting both lines |
| MSI profile, tag | None; bare `5.7.0`, printed | The convention every release so far has set | An MSI; `v5.7.0` |
| Breakdown | One item, one dev, last in the run | A release is one reviewable commit, and it must describe what the run built | Splitting the notes from the version |
| Integration path in the runbook | Run branch → `develop` → `main`, then tag | The run started from `develop` @ `5ea9463` | Merging straight into `main` |
