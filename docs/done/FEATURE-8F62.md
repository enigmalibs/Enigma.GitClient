# FEATURE-8F62 — Release 1.1.0

**Item:** FEATURE-8F62 — Release 1.1.0
**Branch:** `feature/feature-8f62-release-1-1-0`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Summary

Version **1.1.0** is cut in the repository. The merges, the tag and the pushes are left for the user
and are printed below.

**Why 1.1.0.** 1.0.0 is released. Under Semantic Versioning, this run adds new behaviour that stays
backward compatible, a visual refresh and a minor dependency bump, and removes nothing. No setting,
file or token format changes. That makes it a **MINOR** release:
- not 1.0.1, because a patch is for fixes only;
- not 2.0.0, because nothing breaks.

What the release contains:

- `Directory.Build.props` — `<Version>1.1.0</Version>` for Core and App together. The built
  assemblies carry the informational version `1.1.0+<commit>`, which the splash screen, the About
  dialog, the Settings page and the hosting user agent read.
- `RELEASENOTES.md` — `## 1.1.0 — 2026-09-27` on top, in the repository's themed shape:
  - *Notifications* — nothing waits on a bar; success and info close after 5 s; a failed history
    read no longer stays busy.
  - *The diff* — pastel, visible tints; readable chips.
  - *Working with the repository* — the dialogs on the library's secondary surface, same look.
  - *Dependencies* and *Version*.
- `README.md` — the callout is now *What's new in 1.1*.
- `SECURITY.md` — 1.1.x supported, 1.0.x no longer.
- `docs/RELEASE.md` — the MSI paragraph records that 1.1.0 declined a profile too.

## Files / modules touched

**Modified**

- `Directory.Build.props` — `1.0.0 → 1.1.0`
- `RELEASENOTES.md` — the 1.1.0 section
- `README.md` — the what's-new callout
- `SECURITY.md` — the supported-versions table
- `docs/RELEASE.md` — "declined for 1.0.0 and 1.1.0"
- `docs/roadmap.md`, `docs/plan/FEATURE-8F62.md` — statuses

## Dependency refresh

`dotnet list Enigma.GitClient.slnx package --outdated`, run on the release branch:

| Package | Current | Latest | Action |
|---|---|---|---|
| Enigma.Avalonia.Desktop | 1.1.0 (from 1.0.0, in FEATURE-A5D3-PHASE01) | 1.1.0 | — |
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.1 | 12.1.3 | **held back**: the coupled set Enigma.Avalonia.Desktop 1.1.0 is built against, bumped as a whole or not at all |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.3 | held back with the set |
| everything else (Microsoft.Extensions.*, CommunityToolkit.Mvvm, Enigma.Icons.Avalonia, xunit.v3, …) | — | — | already latest |

Nothing non-coupled was outdated, so `Directory.Packages.props` is not touched by this dev.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| A *Version* sub-section, which 1.0.0's notes lack | Added, with the SemVer reason | `dotnet-release`'s order, *… Dependencies · Compatibility · Version*. 1.0.0 had no earlier version to be measured against |
| A *Compatibility* sub-section | None | Platforms, git floor and .NET are unchanged. 1.0.0's section still holds |
| `SECURITY.md` for 1.0.x | Marked unsupported | The file's own policy: fixes go to the latest release |
| Licence audit | Not repeated | Routine release. Enigma.Avalonia.Desktop 1.1.0 keeps its MIT licence and exactly 1.0.0's dependencies (Avalonia 12.1.1, CommunityToolkit.Mvvm 8.4.2, Enigma.Core 1.0.0, from its release notes and nuspec), so nothing new is redistributed |
| MSI profile | None | Declined for 1.0.0, and nothing about Windows packaging changed |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev, repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-09-27-infobars-dialogs-diff-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, the convention 1.0.0 set (there is no local tag yet; tag 1.0.0 on its
#    release merge first if you want it in the history too)
git tag 1.1.0

# 4. Push
git push origin develop main
git push origin 1.1.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md` §7:
- the launcher entry starts the application;
- the splash screen and the About dialog say `Version 1.1.0`;
- `git tag` lists `1.1.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Follow-up: `git tag` lists nothing locally, so 1.0.0 was never tagged in this clone (its runbook
  printed the command). Tagging it is the user's call; see step 3.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update. A search for `1.0.0` / `1.0.x` / "in 1.0" across the README,
`SECURITY.md`, `docs/RELEASE.md`, `packaging/` and `src/` leaves only the historical MSI sentence and
the tag dialog's `v1.0.0` placeholder, which is an example and not the app's version.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors, no `AVLN` warnings.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2231 passed**, 0 failed, with no fix
  cycle.
- `strings …/bin/Release/net10.0/Enigma.GitClient.Core.dll` and `Enigma.GitClient.App.dll` both give
  `1.1.0+265668c8adc70b57e1825ae34d87851057520f06`, the commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
