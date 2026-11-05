# FEATURE-2D0A — Release 6.16.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-2d0a-release-6-16-0`
**Run:** feature/2026-11-05-history-badges-release

## Objective

Cut **6.16.0**, using FEATURE-69DF (6.15.0) as the template and following the `dotnet-release` skill.
The release covers:

- the version;
- the release notes for what 6.16.0 adds beyond 6.15.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.15.0` today).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.16.0 (z is 0).
- **`buildmsi.ps1` has no version of its own**: it reads `<Version>` from `Directory.Build.props`.
- **6.16.0 carries** the run's other items that are `DONE` when this dev starts: FEATURE-D7D5,
  FEATURE-F452, FEATURE-70CB, FEATURE-0123. A quarantined one is left out of the notes.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Linux):** the whole suite runs, the Desktop one included.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1` (it starts
  with `git clean -dxf`).

## Steps

1. Check that `git tag --list 6.16.0` is empty.
2. `Directory.Build.props`: `<Version>6.16.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.16.0` section on top, in 6.15.0's shape: a summary list, one
   section per change, *Upgrading from 6.15*, *Dependencies*, *Version*. Every path it quotes is
   checked against the files.
4. `README.md`: the callout names 6.16 and sums up 6.15 after it; the oldest sentence goes, as in
   6.15.0.
5. `SECURITY.md`: 6.16.x supported; 6.15.x no longer.
6. `docs/RELEASE.md`: any line naming the current version is brought to 6.16.0; history stays.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied; the Avalonia set is held unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: a clean build with zero warnings, the whole suite green against the
   baseline, the assemblies carrying `6.16.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the whole suite shows no new failure.
- The built assemblies carry 6.16.0.
- `RELEASENOTES.md` covers every `DONE` item of this run, and its quoted paths match the files.
- The README callout names 6.16. `SECURITY.md` supports 6.16.x.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack`, `buildmsi`, or installs
  an MSI.

## Out of scope

- NuGet packaging; building the MSI; moving the Avalonia set on its own.
- Merging into `develop`/`main` and tagging: the user's, after the run.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.16.0 | The user's rule, x.y+1.z with z = 0 | 6.15.1; 7.0.0 |
| Order in the run | Last | The notes describe the items built first | First |
| Shape of the notes, callout, tag | As 6.15.0 | House convention | A single *Changes* list |
| Installer | No edit to `buildmsi`; the `<Version>` bump is what it builds | It reads `Directory.Build.props` | Hard-coding the version in the script |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | House rule from earlier releases | Bumping everything |
| Tests | The whole suite, Desktop included | This is the Linux machine the runbook runs the Desktop suite on | Compile-only Desktop |
