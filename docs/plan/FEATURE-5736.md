# FEATURE-5736 — Release 6.12.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-5736-release-6-12-0`
**Run:** feature/2026-10-28-new-app-icon

## Objective

Cut **6.12.0**, using FEATURE-7CBA (6.11.0) as the template and following the `dotnet-release` skill:

- the version;
- the release notes for what 6.12.0 adds beyond 6.11.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.11.0` today).
- **6.11.0 is tagged** on `main` (`9237bf2`, the merge of `develop` at `fc4dfc2`); the run branch is
  cut from `develop` at `fc4dfc2`.
- **6.12.0 carries one item**, FEATURE-F7B5, `DONE` on this run branch before this dev starts: the new
  application icon (the Soft palette, ten `.ico` frames, eight Linux sizes).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).0` — 6.11.0 → 6.12.0. It is also correct
  under Semantic Versioning: nothing incompatible changes, and no stored format moves
  (`git diff fc4dfc2 HEAD -G CurrentVersion` must be empty).
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against.
- **Tests on this machine (Linux):** the whole suite runs, the Desktop suite included.
- Never tag, push or merge into `develop`/`main` inside the run.

## Steps

1. Check that `git tag --list 6.12.0` is empty.
2. `Directory.Build.props`: `<Version>6.12.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.12.0` section on top, in 6.11.0's shape: a summary list, then
   *A new application icon*, *Upgrading from 6.11*, *Dependencies*, *Version*. Every size, colour and
   path the notes quote is checked against the files.
4. `README.md`: the callout names 6.12 and sums up 6.11 after it; 6.10's summary goes; the older
   pointers (5.9, 5.1.0, 4.x) stay.
5. `SECURITY.md`: 6.12.x is supported; 6.11.x no longer is.
6. `docs/RELEASE.md`: the MSI paragraph lists 6.12.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch and minor bumps are
   applied. The Avalonia set is held back unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release: a clean build with zero warnings; the whole suite green; the assemblies carry
   `6.12.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the whole suite is green.
- The built assemblies carry 6.12.0.
- `RELEASENOTES.md` covers FEATURE-F7B5, and its quoted sizes, colours and paths match the files.
- The README callout names 6.12. `SECURITY.md` supports 6.12.x. `docs/RELEASE.md` lists 6.12.0 among
  the releases without an MSI profile.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set on its own.
- Merging into `develop`/`main` and tagging: done after the run, at the user's explicit request.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.12.0 | The user's rule (y+1, z reset to 0); a minor release under SemVer too | 6.11.1 |
| What the release carries | FEATURE-F7B5 | The only change since 6.11.0 (`fc4dfc2`) | — |
| Shape of the notes, callout, MSI, tag | As 6.11.0 (minor, per-area sections) | House convention | A single *Changes* list |
| Breakdown | One phase | A routine release, as every release since 1.0.0 | Splitting the docs from the version |
| `SECURITY.md` scope | Unchanged beyond the supported line | Image files and two size lists; the installer writes the same kind of files to the same directories | Naming the icon change |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | The house rule from earlier releases | Bumping everything |
| MSI profile | Declined | The releases' packaging is the Linux installer | Generating a first profile |
| Tests | The whole suite, the Desktop suite included | This machine is Linux | Core suites only |
