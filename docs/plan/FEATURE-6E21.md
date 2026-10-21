# FEATURE-6E21 — Release 6.5.0

**Status:** DONE — see `docs/done/FEATURE-6E21.md`
**Type:** FEATURE
**Branch:** `feature/feature-6e21-release-6-5-0`
**Run:** feature/2026-10-21-ui-polish-release-650

## Objective

Cut **6.5.0**, using FEATURE-C56E (6.4.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.5.0 adds beyond 6.4.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.4.0` today).
- **6.5.0 carries four items**, all built earlier in this run:
  - **FEATURE-1DC2**: new repositories join the top of the list;
  - **FEATURE-1585**: the info bar sits in the bottom-left corner, a quarter of the window wide;
  - **FEATURE-5595**: the branch tree opens fully; *Expand all* / *Collapse all*;
  - **FEATURE-1085**: lighter, larger details on branch and remote lines.
  - Any of them quarantined is left out of the notes, and the release still goes ahead.
- **Semantic Versioning:** a minor release (new, backward-compatible behaviour). No stored format
  changes: `settings.json` and `repository-lists.json` keep their versions.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine:** the Core suites run; the Desktop suite is compiled, **not run**. The
  runbook says to run it on Linux before tagging.
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.5.0` is empty.
2. `Directory.Build.props`: `<Version>6.5.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.5.0` section on top, in 6.4.0's shape. A summary list, then one
   section per area:
   - *Repositories list*;
   - *Notifications*;
   - *Branches*;
   - *Upgrading from 6.4*;
   - *Dependencies*;
   - *Version*.
4. `README.md`: the callout names 6.5 and sums up 6.4 after it. 6.3's summary goes; the older pointers
   stay.
5. `SECURITY.md`: 6.5.x is supported; 6.4.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.5.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the Core suites green;
   - the Desktop suite compiled;
   - the assemblies carry `6.5.0`.
9. Print the runbook:
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.5.0`, push, publish;
   - before tagging, run the Desktop suite on Linux and check the four changes by hand.

## Acceptance criteria

- The Release build is clean with zero warnings. The Core suites are green. The Desktop suite
  compiles.
- The built assemblies carry 6.5.0.
- `RELEASENOTES.md` covers every item of this run that reached `DONE`.
- The README callout names 6.5. `SECURITY.md` supports 6.5.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.5.0 | Yours; also correct by SemVer (new behaviour, nothing broken, no format change) | — |
| Shape of the notes, callout, MSI, tag | As 6.4.0 (FEATURE-C56E) | House convention | A new layout |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| Tests | Core run, Desktop compiled; a Linux run and manual checks in the runbook | Standing instruction for this machine | Running the Desktop suite here |
| Order | Last item of the run | It describes the four items before it | First |
