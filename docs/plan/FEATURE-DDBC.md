# FEATURE-DDBC — Release 6.6.0

**Status:** DONE — see `docs/done/FEATURE-DDBC.md`
**Type:** FEATURE
**Branch:** `feature/feature-ddbc-release-6-6-0`
**Run:** feature/2026-10-21-release-660

## Objective

Cut **6.6.0**, using FEATURE-6E21 (6.5.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.6.0 adds beyond 6.5.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.5.0` today).
- **6.6.0 carries one item:** **BUG-1E83**, the diff no longer freezes the application when a file
  with no grammar is selected or a file is deselected. It was built by the run
  `bugfix/2026-10-21-diff-grammar-deadlock`, which this run branch is cut from.
- **6.5.0 is not tagged yet** (the last tag is `6.4.0`). Its section in `RELEASENOTES.md` stays as it
  is; the runbook says how to tag it before 6.6.0 if you still want it as its own release.
- **Semantic Versioning:** 6.6.0 is the number you asked for. A fix alone could ship as 6.5.1; the
  notes say so, as 5.7.0's did. No stored format changes: `settings.json` stays at version 7 and
  `repository-lists.json` at version 1.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine:** the Core suites run; the Desktop suite is compiled, **not run**. The
  runbook says to run it on Linux before tagging.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.6.0` is empty.
2. `Directory.Build.props`: `<Version>6.6.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.6.0` section on top, in 5.7.0's fix-only shape:
   - a summary;
   - *Fixes*;
   - *Upgrading from 6.5*;
   - *Dependencies*;
   - *Version*.
4. `README.md`: the callout names 6.6 and sums up 6.5 after it. 6.4's summary goes; the older
   pointers stay.
5. `SECURITY.md`: 6.6.x is supported; 6.5.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.6.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the Core suites green;
   - the Desktop suite compiled;
   - the assemblies carry `6.6.0`.
9. Print the runbook:
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.6.0` (and `6.5.0` on its own commit, if wanted), push, publish;
   - before tagging, run the Desktop suite on Linux and check the fix by hand.

## Acceptance criteria

- The Release build is clean with zero warnings. The Core suites are green. The Desktop suite
  compiles.
- The built assemblies carry 6.6.0.
- `RELEASENOTES.md` covers BUG-1E83.
- The README callout names 6.6. `SECURITY.md` supports 6.6.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.
- Reporting the TextMate lock-order inversion upstream (yours to do).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.6.0 | Yours. The notes say a patch (6.5.1) could have carried it, as 5.7.0's did | 6.5.1 (not what you asked) |
| What the release carries | BUG-1E83 only | The only change since 6.5.0's commit | Folding 6.5.0's items into 6.6.0 (6.5.0 has its own section) |
| 6.5.0, untagged | Left as its own section; the runbook offers tagging it on its own commit | Keeps both releases' notes true whichever you publish | Merging the two sections |
| Shape of the notes, callout, MSI, tag | As 5.7.0 (fix-only) and 6.5.0 | House convention | A new layout |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| Tests | Core run, Desktop compiled; a Linux run and a manual check in the runbook | Standing instruction for this machine | Running the Desktop suite here |
| Run branch base | Cut from `bugfix/2026-10-21-diff-grammar-deadlock` | The release must carry the fix | Cutting from `develop` (the fix is not merged there yet) |
