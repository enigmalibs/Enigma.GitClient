# FEATURE-2B7B — Release 1.0.0

**Status:** DONE — see `docs/done/FEATURE-2B7B.md`
**Type:** FEATURE
**Branch:** `feature/feature-2b7b-release-1-0-0`
**Run:** feature/2026-09-26-release-1-0-0

## Objective

Cut version **1.0.0**: the version stated once for the whole solution, the release notes dated and
complete, the README's what's-new callout, a release runbook in `docs/RELEASE.md`, a `SECURITY.md`,
and the tag/merge commands printed for the user to run.

## Context & constraints

- **An app, not a package.** No project has a `PackageId`; per `dotnet-release`'s *App vs library*
  rule the version bump, the release notes, the README callout and the tag apply, and `dotnet pack` /
  `dotnet nuget push` and the packable-library prerequisites do not.
- There is no `<Version>` anywhere, so every assembly builds as `1.0.0.0` by accident;
  `ProductInformation.GetVersion()` reads the informational version, which the splash, the About
  dialog, the Settings page and the user agent show.
- `RELEASENOTES.md` has a `## 1.0.0 — unreleased` section in the repository's own style (themed
  sub-sections: *The graph*, *The diff*, *Working with the repository*, …). Earlier devs of this run
  add to it through the documentation sweep; this one dates it and fills what is missing.
- The repository has no tags (the default tag format is then bare `X.Y.Z`), a GitHub remote `origin`,
  a default branch `main`, and work integrated on `develop`.
- A run never tags, pushes or merges into `develop`/`main`: those commands are printed.
- The Linux installer (FEATURE-B4C0) distributes binaries — the runtime licence audit belongs to this
  release.
- `docs/RELEASE.md` and `SECURITY.md` do not exist; the `dotnet-release` templates are created, not
  diffed.

## Steps

1. `Directory.Build.props` — `<Version>1.0.0</Version>`, with a comment saying Core and App version
   together because they ship as one application.
2. `RELEASENOTES.md` — the section becomes `## 1.0.0 — 2026-09-26`; check it covers everything this
   run shipped (tool dialogs, splash screen, About dialog, hidden branches, the Linux installer, the
   removed History controls) and add a *Compatibility* sub-section (Linux and Windows, git 2.20 or
   newer, .NET 10).
3. `README.md` — the what's-new callout under the intro.
4. `docs/RELEASE.md` — from the `dotnet-release` template, adapted to an app: pre-flight build and test
   in Release, merge `develop` into `main`, tag `1.0.0`, push the tag, install on Linux with the
   installer, verify; the pack and push steps dropped, and why.
5. `SECURITY.md` — from the template, for a public repository whose app stores access tokens.
6. The runtime licence audit, recorded in the completion doc: every package the published application
   redistributes and its licence, read from the package.
7. Print the runbook for the user.

## Acceptance criteria

- `dotnet build Enigma.GitClient.slnx -c Release` is clean with zero warnings and
  `dotnet test --solution Enigma.GitClient.slnx -c Release` is green.
- The built assemblies carry version `1.0.0` and the application reports "1.0.0" (not a fallback).
- `RELEASENOTES.md` carries one dated `1.0.0` section covering the release; the README has the
  callout; `docs/RELEASE.md` and `SECURITY.md` exist and every command in the runbook runs against
  this repository.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging and publishing.
- A Windows MSI profile.
- A dependency refresh: the Avalonia set is pinned to the one Enigma.Avalonia.Desktop 1.0.0 was built
  against, and nothing else is due for a first release.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the version lives | `Directory.Build.props`, solution-wide | Core and App are one shipped application; one number cannot drift from another (Enigma.Tasks did the same) | Per-project `<Version>`; a `Version.props` |
| Release-notes shape | The repository's own themed sections, dated, plus *Compatibility* | `dotnet-release` says to match an existing style | Rewriting into the template's *Feature overview* shape |
| Tag | Bare `1.0.0`, printed | No existing tag to match; a run never tags | `v1.0.0`; tagging inside the run |
| MSI profile | None this release | Windows installer infrastructure nobody asked for; the release's packaging is the Linux installer | Generating one |
| `SECURITY.md` | Added from the template | The repository is on GitHub and the application keeps personal access tokens; reporting a vulnerability privately should have a stated route | Skipping it |
| Dependency refresh | None | First release; the coupled Avalonia set is pinned by Enigma.Avalonia.Desktop 1.0.0 | Bumping the non-coupled packages now (unrelated churn in a release commit) |
