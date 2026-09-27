# FEATURE-4AC8 — Release 2.0.0

**Item:** FEATURE-4AC8 — Release 2.0.0
**Branch:** `feature/feature-4ac8-release-2-0-0`
**Run:** feature/2026-09-27-diff-profiles-release

## Summary

Version **2.0.0** is cut in the repository. The merges, the tag and the pushes are left for the user
and are printed below.

**Why 2.0.0.** 1.1.0 is released. Under Semantic Versioning, a change that can make previously valid
use fail after an upgrade is incompatible, and that makes it a **MAJOR** release. This run has two:

- **Pushes that 1.1 ran can now be refused.** From a repository that commits as a profile, a push
  needs an integration of that profile for the remote's host. A user with profiles and no
  integrations has to connect them before pushing again.
- **A page is removed.** The Integrations page is gone; what it did moved to the Profiles page.

It also adds features: profiles own their integrations, repositories are browsed in a dialog, and the
diffs open on the first file. The file formats stay compatible both ways. `host-accounts.json`
version 2 only adds a field, which 1.x ignores. That is stated in the notes, but it does not lower the
version:

- not 1.2.0, because a minor release promises that nothing that worked stops working;
- not 1.1.1, because this is not only fixes.

What the release contains:

- `Directory.Build.props` — `<Version>2.0.0</Version>` for Core and App together. The built assemblies
  carry the informational version `2.0.0+<commit>`, which the splash screen, the About dialog, the
  Settings page and the hosting user agent read.
- `RELEASENOTES.md` — `## 2.0.0 — 2026-09-27` on top, in the file's themed shape:
  - *Profiles and their integrations*;
  - *Pushing*;
  - *The diff*;
  - *Upgrading from 1.x* — what can now refuse a push, the earlier integrations to move, and the file
    compatibility both ways;
  - *Dependencies* and *Version*.
- `README.md` — the callout is now *What's new in 2.0*, pointing at the upgrade notes.
- `SECURITY.md` — 2.0.x supported; 1.x (1.0 and 1.1 together) no longer.
- `docs/RELEASE.md` — the MSI paragraph records that 2.0.0 declined a profile too.

## Files / modules touched

**Modified**

- `Directory.Build.props` — `1.1.0 → 2.0.0`
- `RELEASENOTES.md` — the 2.0.0 section
- `README.md` — the what's-new callout
- `SECURITY.md` — the supported-versions table
- `docs/RELEASE.md` — "declined for 1.0.0, 1.1.0 and 2.0.0"
- `docs/roadmap.md`, `docs/plan/FEATURE-4AC8.md` — statuses

## Dependency refresh

`dotnet list Enigma.GitClient.slnx package --outdated`, run on the release branch:

| Package | Current | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.1 | 12.1.3 | **held back**: this is the coupled set Enigma.Avalonia.Desktop 1.1.0 is built against, and it is bumped as a whole or not at all |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.3 | held back with the set |
| everything else (Microsoft.Extensions.*, System.Security.Cryptography.ProtectedData, CommunityToolkit.Mvvm, Enigma.Avalonia.Desktop, Enigma.Icons.Avalonia, xunit.v3, CodeCoverage) | — | — | already latest |

Nothing outside the coupled set was outdated, so `Directory.Packages.props` is not touched.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| MAJOR, although the file formats stay compatible | MAJOR | What an upgrade breaks is behaviour: a push, for users of profiles. SemVer measures that, not only formats. The notes say plainly that going back to 1.1 keeps the accounts |
| An *Upgrading from 1.x* sub-section | Added, before *Dependencies* | A major release has to say what to do. `dotnet-release`'s *Breaking Changes & Migration* slot, named in the file's plain style |
| `SECURITY.md` row for 1.x | One `1.x` row, unsupported | The file's policy: fixes go to the latest release |
| Licence audit | Not repeated | A routine release that adds or changes no dependency |
| MSI profile | None | Declined for 1.0.0 and 1.1.0, and nothing about Windows packaging changed |
| Tag | Bare `2.0.0` | `git tag` lists `1.0.0` and `1.1.0`: the convention holds |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev, repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-09-27-diff-profiles-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 and 1.1.0 are
git tag 2.0.0

# 4. Push
git push origin develop main
git push origin 2.0.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md` §7:

- the launcher entry starts the application;
- the splash screen and the About dialog say `Version 2.0.0`;
- the rails read *Profiles, Settings*;
- `git tag` lists `2.0.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update. A search for `1.1.0` / `1.1.x` across the README, `SECURITY.md`,
`docs/RELEASE.md`, `packaging/` and `src/` leaves only:

- the historical MSI sentence;
- `docs/RELEASE.md`'s `e.g. 1.1.0`, an example of the placeholder;
- `ProductVersion`'s `1.1.0-rc.1` doc example and its test.

None of these is the app's version.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2278 passed**, 0 failed, with no fix
  cycle.
- The generated assembly info of both `Enigma.GitClient.Core` and `Enigma.GitClient.App` (Release)
  carries `AssemblyVersion 2.0.0.0`, `FileVersion 2.0.0.0` and `InformationalVersion
  2.0.0+e32b14f36a772e5260229c5f17e586520134ef59`, the commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
