# FEATURE-4707 — Release 5.4.0

**Item:** FEATURE-4707 — Release 5.4.0
**Branch:** `feature/feature-4707-release-5-4-0`
**Run:** feature/2026-10-05-revert-tree-basedir-release

## Summary

Cuts **5.4.0**, a MINOR release under Semantic Versioning. It ships every other item of this run, all
merged:

- **FEATURE-659E:** *Revert this commit…* in a history line's menu (two phases: the engine, the menu);
- **FEATURE-1CCB:** the changed files open as a tree of folders by default (settings schema 6);
- **FEATURE-1E0D:** a base directory per profile, where Open, Clone and Create start (two phases).

**Why 5.4.0:** it is what was asked, and SemVer agrees: backward-compatible behaviour, nothing removed.
The stored settings move to schema 6, which 5.3 still reads. The one change a user will notice without
asking for it — the files opening as a tree — is called out under *Upgrading from 5.3*.

- `Directory.Build.props`: `<Version>5.4.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.4.0 — 2026-10-05` section on top, in 5.3.0's shape: a summary;
  *The history*; *The changed files*; *The profiles*; *Upgrading from 5.3*; *Dependencies*; *Version*.
- `README.md`: the callout is *What's new in 5.4*; it keeps the 5.1.0 rename pointer and the 4.x one.
- `SECURITY.md`: 5.4.x is supported; 5.3.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.4.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release, on Linux (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-05-revert-tree-basedir-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.3.0 are
git tag 5.4.0

# 4. Push
git push origin develop main
git push origin 5.4.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`. The splash screen and About should say
`Version 5.4.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-4707.md`: statuses

**Created**

- `docs/done/FEATURE-4707.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Sections of the notes | By area (history, changed files, profiles) | 5.3.0's shape; a reader looks for where a change shows |
| How the tree default is described | "Instead of a flat list", "the default of every release so far" | The flat list has been the default since 1.0.0; the first draft's "again, as before 5.0" confused the settings schema's version 5 with the app's |
| Going back to 5.3 | Described: 5.3 reads the newer settings and ignores the base directories, and a profile it saves loses its base directory | Checked in the code: 5.3 reads a newer settings file without migrating it, and the profile store ignores unknown keys |
| Dependency bumps | None | Only the coupled Avalonia set (12.1.1 → 12.1.3) moved, held back with Enigma.Avalonia.Desktop 1.2.0 |
| MSI profile | None, as for every release so far | The packaging is the Linux installer (`docs/RELEASE.md`) |

## Deviations & follow-ups

- None from the plan.
- The revert, the base directory and the tree were exercised by the headless test suite, not by hand;
  worth a click on each before tagging (revert a commit and a merge, pick a profile with a base
  directory and press Open, Clone and Create).
- The Avalonia set (12.1.1 → 12.1.3) is still held back.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists. No other prose doc names the current version.

## Build/test evidence

- `git tag --list 5.4.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: 2790 total, 2790 passed, 0 failed,
  0 skipped — the whole suite, Desktop included, on Linux.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.4.0+084ed0e6683b86c619038c6096a8cde7297e7156`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
