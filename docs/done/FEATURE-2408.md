# FEATURE-2408 — Release 3.0.0 with the refresh fix

**Item:** FEATURE-2408 — Release 3.0.0 with the refresh fix
**Branch:** `feature/feature-2408-release-3-0-0`
**Run:** bugfix/2026-09-27-remote-refresh-release

## Summary

The release is **3.0.0**, now with `BUG-6B9E` in it. The merges, the tag and the pushes are left for the
user and are printed below.

**Why still 3.0.0.** A version number names a release, and 3.0.0 has not been released:

- `git tag --list 3.0.0` is empty;
- `main` is still at the 2.0.0 merge (`471566b`);
- `FEATURE-5CD8` prepared 3.0.0 — the version and the notes — and that run reached `develop` (pushed),
  but a release here is a tag on `main` (`docs/RELEASE.md`).

Semantic Versioning forbids changing the contents of a *released* version. An unreleased one can still
take a fix. The release is compared with the last one, 2.0.0:

- `FEATURE-6C81` is incompatible, which makes it MAJOR (`FEATURE-5CD8`);
- `BUG-6B9E` is a backward-compatible fix, which does not change that.

So 3.0.0 it stays. The alternatives, rejected:

- **3.0.1** would mean tagging a 3.0.0 known to leave the history stale after a push, only to replace
  it straight away: a version every user should skip.
- **3.1.0** would call a fix a feature.

What the release contains:

- `RELEASENOTES.md` — the 3.0.0 section gains *The history*, placed before *Upgrading from 2.0*:
  - the badge follows a toolbar push at once;
  - a pull or a fetch from the toolbar redraws as soon as it ends, keeping the reader's place;
  - whatever moved between two automatic refreshes is redrawn by the next one, every 15 seconds by
    default, as *Fetch and refresh automatically* sets it.
- `Directory.Build.props` stays at `<Version>3.0.0</Version>`.
- `README.md` and `SECURITY.md` are unchanged: the callout names 3.0's headline, the sign-in, and 3.0.x
  is already the supported line.

## Files / modules touched

**Created**

- `docs/done/FEATURE-2408.md`

**Modified**

- `RELEASENOTES.md` — *The history* in the 3.0.0 section
- `docs/roadmap.md`, `docs/plan/FEATURE-2408.md` — statuses

## Dependency refresh

`dotnet list Enigma.GitClient.slnx package --outdated`, run on the release branch:

| Package | Current | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.1 | 12.1.3 | **held back**: the coupled set Enigma.Avalonia.Desktop 1.1.0 is built against, bumped as a whole or not at all |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.3 | held back with the set |
| everything else | — | — | already latest |

The same as `FEATURE-5CD8` found, so the 3.0.0 *Dependencies* sub-section is already correct and
`Directory.Packages.props` is not touched.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| 3.0.0 or 3.0.1 | 3.0.0 | The plan's rule, checked at build time: no `3.0.0` tag exists |
| The date of the 3.0.0 section | Kept, 2026-09-27 | It is today's |
| The intro paragraph of 3.0.0 | Unchanged | It explains why the release is major; the fix is described in its own sub-section |
| Naming the interval's setting | *Fetch and refresh automatically*, on the Settings page | The label the Settings page shows (`SettingsPageView.axaml`) |
| MSI profile, tag | None; bare `3.0.0` | As for every release so far |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev, repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff bugfix/2026-09-27-remote-refresh-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0, 1.1.0 and 2.0.0 are
git tag 3.0.0

# 4. Push
git push origin develop main
git push origin 3.0.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md` §7:

- the launcher entry starts the application;
- the splash screen and the About dialog say `Version 3.0.0`;
- after a push from the toolbar, the remote branch's badge is on the pushed commit;
- `git tag` lists `3.0.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update:

- the README's callout and `SECURITY.md` are still accurate for 3.0.0;
- `docs/RELEASE.md` already records 3.0.0's declined MSI profile.

No other edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2359 passed**, 0 failed, 0 skipped, with
  no fix cycle.
- The generated assembly info of both `Enigma.GitClient.Core` and `Enigma.GitClient.App` (Release)
  carries `AssemblyVersion 3.0.0.0` and `InformationalVersion
  3.0.0+1062c2ff941ad9288009597b7d0958a02aa8c7bf`, the run-branch commit this dev was cut from.
  `strings` on the built `Enigma.GitClient.Core.dll` finds the same.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
