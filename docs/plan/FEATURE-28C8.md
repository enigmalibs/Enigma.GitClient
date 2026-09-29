# FEATURE-28C8 — Release 4.1.1

**Status:** DONE — see `docs/done/FEATURE-28C8.md`
**Type:** FEATURE
**Branch:** `feature/feature-28c8-release-4-1-1`
**Run:** vibe/2026-09-29-dialog-view-type-name

## Objective

Cut **4.1.1**, a PATCH release, once BUG-7E5C is in, following FEATURE-A349 (4.1.0) as the template:

- the version;
- the release notes, naming the fix as a regression from 4.1.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook; a release is a bare
  `X.Y.Z` tag on `main`; the version lives in `Directory.Build.props`.
- **The last release is 4.1.0**, tagged on `main` (`git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0
  4.0.0 4.1.0`). `4.1.1` is free.
- **What this run adds:** fixes only.
  - BUG-7E5C: every dialog whose content is a view showed the view's type name instead of the view,
    a regression shipped in 4.1.0 by BUG-5349.
  - BUG-6EAA: on Windows, saving the settings (or the profiles, the recent repositories, the hidden
    branches) could fail while another instance was reading the same file; and the test suite runs
    on Windows.
- **Fixes only**: nothing added, removed or changed incompatibly, nothing stored changes. By Semantic
  Versioning that is a **PATCH**: **4.1.1**.
- The coupled Avalonia set stays with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. Check `git tag --list 4.1.1` is empty.
2. `Directory.Build.props`: `<Version>4.1.1</Version>`.
3. `RELEASENOTES.md`: a dated `## 4.1.1` section on top, in the file's shape: the summary naming the
   regression from 4.1.0, *Fixes* (the dialogs, then the Windows write), *Upgrading from 4.1.0*,
   *Dependencies*, *Version*.
4. `README.md`: the what's-new callout names 4.1.1 and its fix, and keeps what 4.1 brought.
5. `SECURITY.md`: check that 4.1.x is the supported line — 4.1.1 is inside it.
6. `docs/RELEASE.md`: the MSI paragraph records 4.1.1.
7. Dependency check (`dotnet list package --outdated`): logged; nothing bumped in a PATCH.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `4.1.1`.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 4.1.1.
- `RELEASENOTES.md` describes the fix as a regression from 4.1.0; the README callout names 4.1.1;
  `SECURITY.md` covers 4.1.1.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Any dependency bump.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 4.1.1 | Fixes only — the regression and the Windows write — PATCH | 4.2.0 (nothing new) |
| Dependency refresh | Checked and logged; nothing bumped | A regression hotfix ships only the fix, so a user taking it takes no other change; any update waits for the next minor | FEATURE-A349's rule (non-coupled patch/minor bumps applied) |
| The README callout | "What's new in 4.1.1" naming the fix, then what 4.1 brought | A reader landing on the README sees both the fix and the 4.1 features | Replacing the 4.1 features with the fix alone; leaving the callout at 4.1 |
| MSI profile, tag | None; bare `4.1.1`, printed | The conventions 1.0.0 to 4.1.0 set | Generating an MSI; `v4.1.1` |
