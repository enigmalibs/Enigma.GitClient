# FEATURE-69DF — Release 6.15.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-69df-release-6-15-0`
**Run:** feature/2026-11-03-final3-icons-release

## Objective

Cut **6.15.0**, using FEATURE-AA46 (6.14.0) as the template and following the `dotnet-release` skill.
Make sure the Windows installer script, `buildmsi.ps1`, builds 6.15.0. The release covers:

- the version;
- the release notes for what 6.15.0 adds beyond 6.14.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.14.0` today). Tags go up to `6.14.0`.
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.15.0 (z is 0).
- **`buildmsi.ps1` has no version number of its own.** It reads `/Project/PropertyGroup/Version` from
  `Directory.Build.props`, checks it against `^\d+\.\d+\.\d+$`, and names the MSI and its profile
  `Enigma.GitClient.<version>`. FEATURE-EAB7 designed it that way so that "nothing about the installer is
  edited for a release" (`RELEASENOTES.md` 6.14.0, `docs/RELEASE.md` *The Windows installer's
  identity*). Bumping `<Version>` is what updates the version the script builds.
- **6.15.0 carries** FEATURE-E219, the icon set that fills its frame, `DONE` on the run branch before
  this dev starts. If it ended quarantined, it is left out of the notes.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Windows):** the Core suites run; the Desktop tests are compiled, not run
  (the user's standing rule). The notes' pre-flight says the Desktop suite is to be run on Linux before
  tagging.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1`: it starts
  with `git clean -dxf`.

## Steps

1. Check that `git tag --list 6.15.0` is empty.
2. `Directory.Build.props`: `<Version>6.15.0</Version>`.
3. Check the installer's version: run `buildmsi.ps1`'s version-resolution lines (the same XPath and
   pattern), not the script, against the repository. They yield `6.15.0` and the MSI name
   `Enigma.GitClient.6.15.0`. `buildmsi.ps1` and `buildmsi.bat` are left unchanged.
4. `RELEASENOTES.md`: a dated `## 6.15.0` section on top, in 6.14.0's shape:
   - a summary list;
   - one section per change;
   - *Upgrading from 6.14*, *Dependencies*, *Version*.

   Every path the notes quote is checked against the files.
5. `README.md`: the callout names 6.15 and sums up 6.14 after it; the 6.13 sentence goes.
6. `SECURITY.md`: 6.15.x is supported; 6.14.x no longer is.
7. `docs/RELEASE.md`: any line that names the current version is brought to 6.15.0. Its mentions of
   the MSI that installed 6.13.0 are history and stay.
8. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
9. Pre-flight in Release:
   - a clean build with zero warnings;
   - the Core suites green against the baseline;
   - the assemblies carry `6.15.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the Core suites show no new failure; the Desktop tests
  compile.
- The built assemblies carry 6.15.0.
- `buildmsi.ps1`'s version resolution, run against the repository, yields `6.15.0`.
- `RELEASENOTES.md` covers every `DONE` item of this run, and its quoted paths match the files.
- The README callout names 6.15. `SECURITY.md` supports 6.15.x.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack`, `buildmsi`, or installs
  an MSI.

## Out of scope

- NuGet packaging.
- Building the 6.15.0 MSI (the user runs `buildmsi` after merging).
- Moving the Avalonia set on its own.
- Merging into `develop`/`main` and tagging: done after the run, at the user's request.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.15.0 | The user's rule, x.y+1.z with z = 0 | 6.14.1; 7.0.0 |
| "Update the new version number in the buildmsi script" | Bump `Directory.Build.props`, which the script reads; verify by its own resolution code; leave the script unchanged | The script has no version literal: FEATURE-EAB7 derived it so a release edits nothing for the installer, as the user asked then | Hard-coding `6.15.0` in `buildmsi.ps1` (a second source of truth, and an edit per release) |
| Running `buildmsi` to prove it | No; resolution lines only | It runs `git clean -dxf`, which the run's git rules forbid, and the MSI is the user's to build after merging | Building the MSI in the working tree or a scratch clone |
| Order in the run | Last | The notes describe the icon, built first | First |
| Shape of the notes, callout, tag | As 6.14.0 | House convention | A single *Changes* list |
| A committed MSI profile for 6.15.0 | None | `buildmsi.ps1` generates the profile per build from the version | `msiProfiles/<App>.6.15.0.msipkg.json` |
| Breakdown | One phase | A routine release | Splitting docs from the version |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | House rule from earlier releases | Bumping everything |
| Tests | Core suites run; Desktop compiled only | The user's standing rule for this Windows machine | Running the Desktop suite |
