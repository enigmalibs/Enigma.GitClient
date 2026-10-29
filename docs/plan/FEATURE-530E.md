# FEATURE-530E — Release 6.13.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-530e-release-6-13-0`
**Run:** feature/2026-10-29-uniform-enigma-apps

## Objective

Cut **6.13.0**, using FEATURE-5736 (6.12.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.13.0 adds beyond 6.12.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.12.0` today).
- **6.13.0 carries** the items of this run, `DONE` on the run branch before this dev starts:
  FEATURE-0DED (no splash screen), FEATURE-5096 (`app.ico` in the output directory), FEATURE-0D4E
  (`publish.bat`, `publish.sh`). An item that ended quarantined is left out of the notes.
- **The spec items that needed no change** (recorded here, not in the notes as changes):
  - the About dialog — this application's is the reference the other Enigma apps copy;
  - unhandled exceptions — already caught and shown in a content dialog (BUG-9DE3);
  - Linux `install.sh` / `uninstall.sh` — already there;
  - .NET 8 targets — none; every project targets `net10.0` only;
  - NuGet packages — `dotnet list package --outdated` was clean on 2026-10-29 (re-checked in step 7).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).0` — 6.12.0 → 6.13.0; a minor release under
  SemVer too (no stored format moves).
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Windows):** Core suites run; Desktop tests compiled, not run (user rule) —
  the notes' pre-flight says the Desktop suite is to be run on Linux before tagging.
- Never tag, push or merge into `develop`/`main` inside the run.

## Steps

1. Check that `git tag --list 6.13.0` is empty.
2. `Directory.Build.props`: `<Version>6.13.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.13.0` section on top, in 6.12.0's shape: a summary list, then one
   section per change, *Upgrading from 6.12*, *Dependencies*, *Version*. Every path the notes quote is
   checked against the files.
4. `README.md`: the callout names 6.13 and sums up 6.12 after it; the oldest summary goes as in 6.12.0.
5. `SECURITY.md`: 6.13.x is supported; 6.12.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.13.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: a clean build with zero warnings; the Core suites green; the assemblies carry
   `6.13.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the Core suites are green; the Desktop tests compile.
- The built assemblies carry 6.13.0.
- `RELEASENOTES.md` covers every `DONE` item of this run, and its quoted paths match the files.
- The README callout names 6.13. `SECURITY.md` supports 6.13.x. `docs/RELEASE.md` lists 6.13.0 among
  the releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Merging into `develop`/`main` and tagging: done after the run, at the user's explicit request.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.13.0 | The user's rule (y+1, z reset to 0) | 6.12.1; 7.0.0 |
| What the release carries | This run's `DONE` items | The only changes since 6.12.0 | — |
| No-op spec items | Recorded in this plan, not as notes entries | Nothing changed for them; notes describe changes | Writing "no change" sections |
| Shape of the notes, callout, MSI, tag | As 6.12.0 | House convention | A single *Changes* list |
| Breakdown | One phase | A routine release, as every release since 1.0.0 | Splitting the docs from the version |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer and the publish scripts | Generating a first profile |
| Tests | Core suites run; Desktop compiled only | User rule for this Windows machine | Running the Desktop suite |
