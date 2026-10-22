# FEATURE-3789 — Release 6.7.0

**Status:** DONE — see `docs/done/FEATURE-3789.md`
**Type:** FEATURE
**Branch:** `feature/feature-3789-release-6-7-0`
**Run:** vibe/2026-10-22-release-6-7-0

## Objective

Cut **6.7.0**, using FEATURE-DDBC (6.6.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.7.0 adds beyond 6.6.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge, tag and push runbook, printed for the user.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.6.0` today).
- **6.6.0 is tagged on `main`** (`b541845`), and `develop` (`489fa2b`) holds it.
- **6.7.0 carries two items**, both `DONE` on the run `vibe/2026-10-22-history-ages-win-publish`,
  which this run branch is cut from:
  - **FEATURE-3D77**: when the history shows *How long ago*, each line's age is measured again on
    every automatic refresh;
  - **FEATURE-18E2**: `packaging/windows/publish.ps1` runs the Windows publish
    (`win-x64`, ReadyToRun, framework-dependent) from any directory.
- **Semantic Versioning:** a minor release (new, backward-compatible behaviour and a new script).
  No stored format changes: `settings.json` stays at version 7 and `repository-lists.json` at
  version 1.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine:** the Core suites run; the Desktop suite is compiled, **not run**. The
  runbook says to run it on Linux before tagging (FEATURE-3D77 added five Desktop tests that are
  compiled only).
- Never tag, push or merge into `develop`/`main`: those commands are printed.

## Steps

1. Check that `git tag --list 6.7.0` is empty.
2. `Directory.Build.props`: `<Version>6.7.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.7.0` section on top, in 6.5.0's shape. A summary list, then one
   section per area:
   - *The history's ages*;
   - *Publishing on Windows*;
   - *Upgrading from 6.6*;
   - *Dependencies*;
   - *Version*.
4. `README.md`: the callout names 6.7 and sums up 6.6 after it. 6.5's summary goes; the older
   pointers stay.
5. `SECURITY.md`: 6.7.x is supported; 6.6.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.7.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build with zero warnings, AVLN included;
   - the Core suites green;
   - the Desktop suite compiled;
   - the assemblies carry `6.7.0`.
9. Print the runbook:
   - the run branch into `develop`, then `develop` into `main`;
   - tag `6.7.0`, push, publish;
   - before tagging, run the Desktop suite on Linux and check both items by hand.

## Acceptance criteria

- The Release build is clean with zero warnings. The Core suites are green. The Desktop suite
  compiles.
- The built assemblies carry 6.7.0.
- `RELEASENOTES.md` covers FEATURE-3D77 and FEATURE-18E2.
- The README callout names 6.7. `SECURITY.md` supports 6.7.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Live relative dates outside the history (the commit details dialog, the branches and tags pages):
  FEATURE-3D77's follow-up.
- Tagging, merging into `develop`/`main`, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.7.0 | Yours, and correct under SemVer: new backward-compatible behaviour | 6.6.1 (the ages are a behaviour change, the script a new file) |
| What the release carries | FEATURE-3D77 and FEATURE-18E2 | The only changes since 6.6.0 | — |
| Run branch base | Cut from `vibe/2026-10-22-history-ages-win-publish` | The release must carry both items | Cutting from `develop` (neither item is merged there yet) |
| Shape of the notes, callout, MSI, tag | As 6.5.0 (minor, per-area sections) and 6.6.0 | House convention | A new layout |
| `SECURITY.md` scope | Unchanged | The Windows script publishes; it installs nothing (FEATURE-18E2's sweep) | Adding `packaging/windows/` to the scope |
| README beyond the callout | Unchanged unless something became wrong | Scope discipline; the *Install on Linux* section stays true | A new *Publish on Windows* section |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer; Windows has a publish script, not an installer | Generating a first profile |
| Tests | Core run, Desktop compiled; a Linux run and a manual check in the runbook | Standing instruction for this machine | Running the Desktop suite here |
