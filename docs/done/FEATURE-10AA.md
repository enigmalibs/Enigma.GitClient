# FEATURE-10AA — Release 4.0.0

**Item:** FEATURE-10AA — Release 4.0.0
**Branch:** `feature/feature-10aa-release-4-0-0`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

The release is **4.0.0**. The merges, the tag and the pushes are the user's, and are printed below.

**Why 4.0.0.** 3.1.0 is released: `git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0`, and `4.0.0` is
free. Since 3.1.0 this run adds backward-compatible functionality:

- the commit details dialog and its menu item;
- the one-line diff header;
- the Changes page's way back and Escape;
- the blue back buttons;
- red discards with one plain question;
- discarding the uncommitted work from the history;
- About on the start window's home.

It also adds two fixes: the start page's empty state and the file lines' menus. It **removes**
functionality as well: the Changes page's *Amend* and *Sign off*. A 3.1 user who amended or signed off
in the app can no longer, and has to use git for it. `FEATURE-4AC8` (2.0.0) and `FEATURE-5CD8`
(3.0.0) ruled that an upgrade which can stop an existing workflow is MAJOR. Nothing stored changes.

What the release contains:

- `Directory.Build.props` → `<Version>4.0.0</Version>`.
- `RELEASENOTES.md` — a dated `## 4.0.0 — 2026-09-28` section on top, in the file's themed shape:
  *Commit details · The Changes page · Discarding · The start window · Fixes · Upgrading from 3.x ·
  Dependencies · Version*. It covers every item of the run.
- `README.md`:
  - the *What's new in 4.0* callout;
  - the features list: a new bullet for the commit details dialog; the working-directory bullet
    names the way back, the red confirmations and the discard from the uncommitted line.
- `SECURITY.md` — 4.0.x is the supported line; 3.x (3.1.x and 3.0.x) is no longer.
- `docs/RELEASE.md` — the MSI paragraph records 4.0.0 too, and is re-wrapped: 3.1.0's edit had left
  a 114-character line.

## Files / modules touched

**Created**

- `docs/done/FEATURE-10AA.md`

**Modified**

- `Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-10AA.md` — statuses

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
| 4.0.0 or another number | 4.0.0 | The plan's rule, checked at build time: no `4.0.0` tag exists, and the removal of *Amend* and *Sign off* is incompatible |
| The upgrade note | How to amend and sign off with git, the simpler discard question, and that nothing is migrated | What a 3.1 user needs to know before upgrading, in the shape 3.0's *Upgrading from 2.0* set |
| README features | One bullet added, one extended, none rewritten | Each new capability belongs on the landing page's list |
| SECURITY table | 4.0.x supported; 3.x, 2.x, 1.x not | "Security fixes are provided for the latest released version", as the file says |
| MSI profile, tag | None; bare `4.0.0`, printed | The conventions 1.0.0 to 3.1.0 set, recorded in `docs/RELEASE.md` |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev; repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff bugfix/2026-09-28-changes-commit-details-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 3.1.0 are
git tag 4.0.0

# 4. Push
git push origin develop main
git push origin 4.0.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- the launcher starts the application;
- the splash screen and the About dialog (now also on the start window's Repositories page) say
  `Version 4.0.0`;
- `git tag` lists `4.0.0` on the merge commit on `main`, and the tag is on the remote.

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
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2490 passed**, 0 failed, 0 skipped, with
  no fix cycle.
- The generated assembly info of `Enigma.GitClient.Core` and `Enigma.GitClient.App` (Release) carries
  `AssemblyVersion 4.0.0.0` and `InformationalVersion 4.0.0+9aeafc3a77735bca88747ec899d1da5e6e4f4ac2`.
  That is the run-branch commit this dev was cut from. `strings` on the built `Enigma.GitClient.Core.dll`
  finds the same.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
