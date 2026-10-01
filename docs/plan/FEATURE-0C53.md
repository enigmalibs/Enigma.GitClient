# FEATURE-0C53 — Release 5.1.0

**Status:** DONE — see `docs/done/FEATURE-0C53.md`
**Type:** FEATURE
**Branch:** `feature/feature-0c53-release-5-1-0`
**Run:** feature/2026-10-01-polish-release-5-1

## Objective

Cut **5.1.0** once this run's other items are in, following the previous releases (FEATURE-5DFF for
5.0.0, FEATURE-A349 for 4.1.0) as the template:

- the version;
- the release notes for everything since 5.0.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook. A release is a bare
  `X.Y.Z` tag on `main`, and the version lives in `Directory.Build.props` (`5.0.0` today).
- **Tags:** `1.0.0` … `5.0.0`. `RELEASENOTES.md`'s top section is 5.0.0.
- **In this release (this run):**
  - FEATURE-85E2: Enigma.Avalonia.Desktop 1.2.0, softer info bar colours in Dark;
  - FEATURE-45D3: success and info bars close after 2.5 s;
  - BUG-15B8: the repository browser no longer cut off;
  - FEATURE-A35A: a click on the selected file lets go of it and closes its diff;
  - BUG-546B: the tool dialogs' headers and the merge band stay at the top.
- **Semantic Versioning:** nothing is removed and nothing stored changes, so **MINOR**. 5.1.0 is what
  was asked, and it agrees.
- `SECURITY.md` supports the latest minor line only (5.0.x today).
- The Avalonia set (12.1.1 → 12.1.3 available) moves only as a whole, with what
  Enigma.Avalonia.Desktop is built against. 1.2.0 is built against 12.1.1.
- A run never tags, pushes or merges into `develop`/`main`. Those commands are printed.

## Steps

1. Check `git tag --list 5.1.0` is empty.
2. `Directory.Build.props`: `<Version>5.1.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.1.0` section on top, in the file's shape:
   - a summary;
   - one sub-section per area: the history's details panel, the dialogs, the info bars;
   - *Fixes*;
   - *Upgrading from 5.0* (nothing to migrate);
   - *Dependencies* (Enigma.Avalonia.Desktop 1.2.0; the Avalonia set held);
   - *Version*.
4. `README.md`: the what's-new callout names 5.1 and its highlights.
5. `SECURITY.md`: 5.1.x supported; 5.0.x no longer.
6. `docs/RELEASE.md`: the MSI paragraph records 5.1.0 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Non-coupled patch/minor bumps applied.
   The Avalonia set is held back.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `5.1.0`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 5.1.0.
- `RELEASENOTES.md` covers every item of the run and the upgrade. The README callout names 5.1, and
  `SECURITY.md` supports 5.1.x.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Moving the Avalonia set.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.1.0 | As asked. Nothing is removed and nothing stored changes, so SemVer MINOR agrees | 5.0.1 (the file toggle is new behaviour) |
| Dependency refresh | Checked and logged; non-coupled bumps applied; the Avalonia set held | The house rule since 1.0.0. 1.2.0 is still built against 12.1.1 | Moving the set |
| The README callout | "What's new in 5.1": the file toggle, the fixed headers, the shorter, softer info bars | A reader landing on the README sees the headline changes | Listing every change |
| `SECURITY.md` | 5.1.x supported, 5.0.x unsupported | The file's own rule: the latest minor line | Supporting both lines |
| MSI profile, tag | None; bare `5.1.0`, printed | The conventions 1.0.0 to 5.0.0 set | Generating an MSI; `v5.1.0` |
