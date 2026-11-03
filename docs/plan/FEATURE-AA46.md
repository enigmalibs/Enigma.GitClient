# FEATURE-AA46 — Release 6.14.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-aa46-release-6-14-0`
**Run:** feature/2026-11-03-icons-msi-release

## Objective

Cut **6.14.0**, using FEATURE-530E (6.13.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.14.0 adds beyond 6.13.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.13.0` today).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).0` (z = 0 already) → 6.14.0.
- **6.14.0 carries** the items of this run, `DONE` on the run branch before this dev starts:
  - FEATURE-35EB: the new icon set without the tile;
  - FEATURE-EAB7: `buildmsi.ps1` / `buildmsi.bat` and the vendored Enigma.Msi worker.
  An item that ended quarantined is left out of the notes.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Windows):** Core suites run; Desktop tests compiled, not run (the user's
  standing rule). The notes' pre-flight says the Desktop suite is to be run on Linux before tagging.
- Never tag, push or merge into `develop`/`main` inside the run.

## Steps

1. Check that `git tag --list 6.14.0` is empty.
2. `Directory.Build.props`: `<Version>6.14.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.14.0` section on top, in 6.13.0's shape: a summary list, then one
   section per change, *Upgrading from 6.13*, *Dependencies*, *Version*. Every path the notes quote is
   checked against the files.
4. `README.md`: the callout names 6.14 and sums up 6.13 after it; the 6.12 sentence goes.
5. `SECURITY.md`: 6.14.x is supported; 6.13.x no longer is.
6. `docs/RELEASE.md`: any version-specific line that names 6.13.0 is brought to 6.14.0. The Windows
   installer step is the one FEATURE-EAB7 wrote.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: a clean build with zero warnings; the Core suites green against the baseline;
   the assemblies carry `6.14.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the Core suites show no new failure; the Desktop tests
  compile.
- The built assemblies carry 6.14.0.
- `RELEASENOTES.md` covers every `DONE` item of this run, and its quoted paths match the files.
- The README callout names 6.14. `SECURITY.md` supports 6.14.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack`, `dotnet nuget push`, or installs the
  MSI.

## Out of scope

- NuGet packaging.
- Building the 6.14.0 MSI for distribution (the user runs `buildmsi` after merging).
- Moving the Avalonia set on its own.
- Merging into `develop`/`main` and tagging: done after the run, at the user's request.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.14.0 | The user's rule (y+1, z reset to 0) | 6.13.1; 7.0.0 |
| Order in the run | Last | The notes describe the icon and the installer, built first | First |
| Shape of the notes, callout, tag | As 6.13.0 | House convention | A single *Changes* list |
| A committed MSI profile for 6.14.0 | None | FEATURE-EAB7's script generates the profile per build from the version | `msiProfiles/<App>.6.14.0.msipkg.json` |
| Breakdown | One phase | A routine release | Splitting docs from the version |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | House rule from earlier releases | Bumping everything |
| Tests | Core suites run; Desktop compiled only | The user's standing rule for this Windows machine | Running the Desktop suite |
