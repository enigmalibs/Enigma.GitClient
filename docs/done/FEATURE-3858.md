# FEATURE-3858 — Release 5.3.0

**Item:** FEATURE-3858 — Release 5.3.0
**Branch:** `feature/feature-3858-release-5-3-0`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

Cuts **5.3.0**, a MINOR release under Semantic Versioning. It ships every other item of this run, all
merged:

- **FEATURE-903B:** the start window's picker switches git's identity; *Use* selects the profile;
- **BUG-6DB7:** the automatic refresh keeps the folders closed in the uncommitted files;
- **FEATURE-56BB:** the repository window opens maximised and centred;
- **FEATURE-6F35:** the uncommitted line is joined to HEAD's commit by a dashed line (two phases);
- **BUG-123A:** a repository opens on its history;
- **BUG-3D1F:** the column titles stop at the details panel;
- **FEATURE-3B4E:** toolbar buttons for the repository's folder and a terminal in it;
- **FEATURE-426F:** a fetch when a repository opens (and the check that both refreshes fetch);
- **FEATURE-6108:** a clone offers the global identity.

**Why 5.3.0:** it is what was asked, and SemVer agrees: backward-compatible behaviour and fixes, nothing
removed, nothing stored changes. The one deliberate change of behaviour — the picker now writes git's
global configuration — is called out under *Upgrading from 5.2*.

- `Directory.Build.props`: `<Version>5.3.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.3.0 — 2026-10-05` section on top, in 5.2.0's shape: a summary;
  *The profiles*; *The history*; *The repository window*; *Upgrading from 5.2*; *Dependencies*;
  *Version*.
- `README.md`: the callout is *What's new in 5.3*; it keeps the 5.1.0 rename pointer and the 4.x one.
- `SECURITY.md`: 5.3.x is supported; 5.2.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.3.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff vibe/2026-10-05-profiles-graph-fetch-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.2.0 are
git tag 5.3.0

# 4. Push
git push origin develop main
git push origin 5.3.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`. The splash screen and About should say
`Version 5.3.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-3858.md`: statuses

**Created**

- `docs/done/FEATURE-3858.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Sections of the notes | By area (profiles, history, repository window), fixes inside their area | 5.2.0's shape; a reader looks for where a change shows |
| The fetch question | Answered in the notes: both refreshes already fetched | The user asked; the notes are where a reader of the release finds it |
| Dependency bumps | None | Only the coupled Avalonia set (12.1.1 → 12.1.3) moved, held back with Enigma.Avalonia.Desktop 1.2.0 |
| MSI profile | None, as for every release so far | The packaging is the Linux installer (`docs/RELEASE.md`) |

## Deviations & follow-ups

- **The Release pre-flight ran on this Windows host without the full Desktop suite** (the user asked to
  skip it: it never finishes on Windows — BUG-6EAA). Before tagging, run step 1 of the runbook on Linux,
  where the whole suite runs, as for 5.2.0.
- FEATURE-3B4E's launches (file manager, terminals) were not tried by hand; worth a click on each
  platform before tagging.
- The Avalonia set (12.1.1 → 12.1.3) is still held back.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists. No other prose doc names the current version.

## Build/test evidence

- `git tag --list 5.3.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests` (Release): 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests` (Release): 344 total, 342 passed, 0 failed, 2 skipped.
- Desktop, targeted (`AboutDialogTests`, `SplashScreenTests`, `ApplicationBootstrapTests`,
  `AssemblyNameConsistencyTests`, `SettingsPageTests`): 61 total, 61 passed.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.3.0+53edadd82bd7602ee9bec4a98adf57e888810847`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
