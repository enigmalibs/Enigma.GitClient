# FEATURE-4AC8 — Release 2.0.0

**Status:** DONE — see `docs/done/FEATURE-4AC8.md`
**Type:** FEATURE
**Branch:** `feature/feature-4ac8-release-2-0-0`
**Run:** feature/2026-09-27-diff-profiles-release

## Objective

Cut version **2.0.0** once `FEATURE-8CC5` and `FEATURE-1406` are in. That means:

- the version stated once for the whole solution;
- a dated `2.0.0` section in the release notes, with an upgrade note;
- the README's what's-new callout;
- the supported version in `SECURITY.md`;
- a dependency refresh;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook, parameterised by
  `X.Y.Z`. 1.1.0 (`FEATURE-8F62`) is the template for this item.
- **The version, by SemVer:** 1.1.0 is released. This run adds features: profiles own their
  integrations, and the first file is always selected. It also changes behaviour incompatibly:
  - a push that 1.1 ran can now be refused, when the repository's profile has no integration for the
    remote;
  - the Integrations page is gone, and its function moved.
  An upgrade can stop a user's existing workflow until they act. That is a **MAJOR** release: **2.0.0**.
- The file formats stay compatible both ways (`host-accounts.json` v2 only adds a field). That goes in
  the notes' upgrade section, not the version.
- The coupled Avalonia set is held with Enigma.Avalonia.Desktop 1.1.0.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. `Directory.Build.props` — `<Version>2.0.0</Version>`.
2. Dependency refresh:
   - run `dotnet list package --outdated`;
   - apply the non-coupled patch/minor bumps;
   - hold the Avalonia set (and anything major) back;
   - log every change and every hold-back.
3. `RELEASENOTES.md` — `## 2.0.0 — <date>` on top, in the file's themed shape, with *Upgrading from 1.x*
   (what can now refuse a push, where the integrations went, earlier integrations to move), and the
   *Dependencies* and *Version* sub-sections.
4. `README.md` — the callout becomes *What's new in 2.0*.
5. `SECURITY.md` — 2.0.x supported, 1.x not.
6. `docs/RELEASE.md` — the MSI paragraph records 2.0.0 too.
7. Pre-flight in Release: build clean, suite green, and the Core assembly carries `2.0.0+<sha>`.
8. Print the runbook.

## Acceptance criteria

- The Release build is clean with zero warnings, and the Release suite is green.
- The built assemblies carry version 2.0.0.
- `RELEASENOTES.md` has one dated `2.0.0` section covering the run, including the upgrade note and the
  dependency changes. The README callout and `SECURITY.md` name 2.0.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major dependency version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 2.0.0 | SemVer MAJOR: an upgrade can refuse pushes 1.1 ran, and a page is removed; users may have to act | 1.2.0 (it hides a behaviour break behind a minor); 1.1.1 (not only fixes) |
| An upgrade note | *Upgrading from 1.x* in the 2.0.0 section | A major release says what to do | Only the feature list |
| Dependency refresh | Non-coupled patch/minor bumps, logged; the Avalonia set and majors held | `dotnet-release`'s rule, as in 1.1.0 | No refresh |
| MSI profile, tag | None; a bare `2.0.0`, printed | The conventions 1.0.0 and 1.1.0 set | Generating an MSI; `v2.0.0` |
