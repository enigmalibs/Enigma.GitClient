# FEATURE-8F62 — Release 1.1.0

**Status:** DONE — see `docs/done/FEATURE-8F62.md`
**Type:** FEATURE
**Branch:** `feature/feature-8f62-release-1-1-0`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Objective

Cut version **1.1.0** once this run's three items are in: the version stated once for the whole
solution, a dated `1.1.0` section in the release notes, the README's what's-new callout, the supported
version in `SECURITY.md`, a dependency refresh, and the merge/tag/push runbook printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`): per `dotnet-release`'s *App vs library* rule, the
  version, the notes, the callout and the tag apply; `dotnet pack` / `dotnet nuget push` do not.
  `docs/RELEASE.md` (written for 1.0.0) is the runbook and already parameterised by `X.Y.Z`.
- **The version, by SemVer:** 1.0.0 is released. This run adds backward-compatible, user-visible
  behaviour (timed, non-blocking notifications), a visual refresh, the secondary dialog surface, and a
  minor dependency bump — nothing removed, no setting or file format changed. That is a **MINOR**
  release: **1.1.0**. Not a patch (these are not only fixes), not a major (nothing breaks).
- `Directory.Build.props` holds `<Version>1.0.0</Version>` for Core and App together.
- `RELEASENOTES.md` is newest-first, `## X.Y.Z — <date>`, in themed sub-sections (*The graph*, *The
  diff*, *Working with the repository*, …); the new section goes on top in the same style.
- `README.md` carries a `> **What's new in 1.0** — …` callout under the intro; `SECURITY.md` names
  1.0.x as supported.
- The coupled Avalonia set is held at 12.1.1: Enigma.Avalonia.Desktop 1.1.0 is built against it.
- The repository has no local tags yet; 1.0.0's runbook set the convention to bare `X.Y.Z`.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.

## Steps

1. `Directory.Build.props` — `<Version>1.1.0</Version>`.
2. Dependency refresh — `dotnet list package --outdated`; apply the non-coupled patch/minor bumps in
   `Directory.Packages.props`; hold the Avalonia set (and anything major) back; log every
   `old → new` and every hold-back.
3. `RELEASENOTES.md` — `## 1.1.0 — 2026-09-27` on top: *Notifications* (timed success/info, nothing
   waits on a bar), *The diff* (pastel, visible colours; readable chips), *Working with the
   repository* (the tool dialogs on the library's secondary surface — no visible change),
   *Dependencies*, *Version*.
4. `README.md` — the callout becomes *What's new in 1.1*.
5. `SECURITY.md` — 1.1.x supported, 1.0.x not.
6. Release-configuration pre-flight: `dotnet build Enigma.GitClient.slnx -c Release` clean,
   `dotnet test --solution Enigma.GitClient.slnx -c Release` green, and the built Core assembly carries
   `1.1.0+<sha>`.
7. Print the runbook (merge into `develop` then `main`, tag `1.1.0`, push, publish, install).

## Acceptance criteria

- The Release build is clean with zero warnings and the Release suite is green.
- The built assemblies carry version 1.1.0 and the application reports 1.1.0.
- `RELEASENOTES.md` carries one dated `1.1.0` section covering the run, with its dependency changes;
  the README callout and `SECURITY.md` name 1.1.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging and publishing; a Windows MSI profile.
- Bumping the coupled Avalonia set, or any major version.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 1.1.0 | SemVer MINOR: new backward-compatible behaviour, nothing removed or broken | 1.0.1 (a patch is fixes only); 2.0.0 (nothing breaks) |
| Dependency refresh | Non-coupled patch/minor bumps, logged; Avalonia set and majors held | `dotnet-release`'s rule; the Avalonia set is pinned by Enigma.Avalonia.Desktop 1.1.0 | No refresh; bumping Avalonia to 12.1.3 alone |
| Release-notes shape | The repository's themed sections, plus *Dependencies* and *Version* | `dotnet-release`: match the existing style | The template's *New Features / Fixes* shape |
| MSI profile | None | Declined for 1.0.0 and recorded in `docs/RELEASE.md`; nothing changed | Generating one |
| Tag | Bare `1.1.0`, printed | The convention 1.0.0 set; a run never tags | `v1.1.0` |
| The licence audit | Not repeated | A routine release; the one new version (Enigma.Avalonia.Desktop 1.1.0) keeps the same MIT licence and dependencies | A full audit |
