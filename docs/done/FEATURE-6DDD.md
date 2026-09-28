# FEATURE-6DDD — Release 3.1.0

**Item:** FEATURE-6DDD — Release 3.1.0
**Branch:** `feature/feature-6ddd-release-3-1-0`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The release is **3.1.0**. The merges, the tag and the pushes are the user's, and are printed below.

**Why 3.1.0.** 3.0.0 is released (`git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0`), and `3.1.0` is free. Since
3.0.0, this run adds backward-compatible functionality:

- stashes in the history;
- drag-to-merge;
- sortable lists;
- a resizable graph column;
- copy and select items;
- menu icons;
- a loader.

It also adds backward-compatible fixes: late badges, Azure DevOps sign-in, and untracked diffs. Nothing
is removed or changed incompatibly, and `settings.json` only gains keys, which an older file reads as
defaults. By Semantic Versioning that is a MINOR release.

What the release contains:

- `Directory.Build.props` → `<Version>3.1.0</Version>`.
- `RELEASENOTES.md` — a dated `## 3.1.0 — 2026-09-28` section on top, in the file's themed shape:
  *Stashes · The history · Branches and tags · Menus · Fixes · Dependencies · Version*. It covers every
  item of the run.
- `README.md`:
  - the *What's new in 3.1* callout;
  - the features list: the graph column, sorting and *Select in the history*, dragging a badge to merge,
    and the stash's own bullet, split out of the Remotes one.
- `SECURITY.md` — 3.1.x is the supported line, and 3.0.x is no longer.
- `docs/RELEASE.md` — records that the MSI profile was declined for 3.1.0 too.

## Files / modules touched

**Created**

- `docs/done/FEATURE-6DDD.md`

**Modified**

- `Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-6DDD.md` — statuses

## Dependency refresh

`dotnet list Enigma.GitClient.slnx package --outdated`, run on the release branch:

| Package | Current | Latest | Action |
|---|---|---|---|
| Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Fonts.Inter | 12.1.1 | 12.1.3 | **held back**: the coupled set Enigma.Avalonia.Desktop 1.1.0 is built against, bumped as a whole or not at all |
| Avalonia.Headless, Avalonia.Skia (tests) | 12.1.1 | 12.1.3 | held back with the set |
| everything else | — | — | already latest |

`Directory.Packages.props` is not touched. The notes' *Dependencies* sub-section says so.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| 3.1.0 or another number | 3.1.0 | The plan's rule, checked at build time: no `3.1.0` tag exists, and nothing is incompatible |
| README features | Four bullets touched, none rewritten | Each new capability belongs on the landing page's list. The stash left the Remotes bullet, since it now has a home of its own. |
| SECURITY table | 3.1.x supported; 3.0.x, 2.x, 1.x not | "Security fixes are provided for the latest released version", as the file says |
| MSI profile, tag | None; bare `3.1.0`, printed | The conventions 1.0.0 to 3.0.0 set, recorded in `docs/RELEASE.md` |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev; repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff bugfix/2026-09-28-history-stash-menus-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 3.0.0 are
git tag 3.1.0

# 4. Push
git push origin develop main
git push origin 3.1.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- the launcher starts the application;
- the splash screen and the About dialog say `Version 3.1.0`;
- `git tag` lists `3.1.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update:

- the README callout and features list;
- `SECURITY.md`;
- `docs/RELEASE.md`'s MSI note;
- `RELEASENOTES.md`.

No other document is affected.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2451 passed**, 0 failed, 0 skipped, with
  no fix cycle.
- The generated assembly info of `Enigma.GitClient.Core` and `Enigma.GitClient.App` (Release) carries
  `AssemblyVersion 3.1.0.0` and `InformationalVersion 3.1.0+9f5804da0710f8571bc3cf84958d78fe614b312d`, the
  run-branch commit this dev was cut from. `strings` on the built `Enigma.GitClient.Core.dll` finds the
  same.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
