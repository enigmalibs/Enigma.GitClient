# FEATURE-A349 — Release 4.1.0

**Item:** FEATURE-A349 — Release 4.1.0
**Branch:** `feature/feature-a349-release-4-1-0`
**Run:** bugfix/2026-09-28-history-tag-push-release

## Summary

The release is **4.1.0**. The merges, the tag and the pushes are the user's, and are printed below.

**Why 4.1.0.** 4.0.0 is released: `git tag` lists `1.0.0 1.1.0 2.0.0 3.0.0 3.1.0 4.0.0`, and `4.1.0` is
free. Since 4.0.0 this run adds:

- one new, backward-compatible feature: pushing one tag, from its history badge or its Tags dialog
  line (FEATURE-A2A2 PHASE02–03);
- a changed suggestion: *Create a tag*'s placeholder reads `1.0.0` (FEATURE-A2A2 PHASE01);
- two fixes: the history's column titles (BUG-29C8), and dialog questions cut off at the edge
  (BUG-5349).

Nothing is removed or changed incompatibly, and nothing stored changes. Under Semantic Versioning
that is a MINOR release. A PATCH (4.0.1) would carry fixes only; a MAJOR has nothing incompatible to
announce.

What the release contains:

- `Directory.Build.props` → `<Version>4.1.0</Version>`.
- `RELEASENOTES.md` — a dated `## 4.1.0 — 2026-09-28` section on top, in the file's themed shape:
  *Tags · Fixes · Upgrading from 4.0 · Dependencies · Version*. It covers every item of the run.
- `README.md` — the *What's new in 4.1* callout. The tag-management bullet was already updated by
  FEATURE-A2A2 PHASE03.
- `SECURITY.md` — 4.1.x is the supported line; 4.0.x is no longer.
- `docs/RELEASE.md` — the MSI paragraph records 4.1.0 too.

## Files / modules touched

**Created**

- `docs/done/FEATURE-A349.md`

**Modified**

- `Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `SECURITY.md`, `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-A349.md` — statuses

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
| 4.1.0 or another number | 4.1.0 | The plan's rule, checked at build time: no `4.1.0` tag exists, and the run adds a feature without breaking anything |
| The upgrade note | Nothing changes in the way you work; nothing migrated; going back to 4.0 keeps the files | What a 4.0 user needs to know, in the shape of *Upgrading from 3.x* |
| The notes' themes | *Tags*, then *Fixes* | The feature and the placeholder are both about tags; the two fixes go under *Fixes*, as in 4.0.0 |
| MSI profile, tag | None; bare `4.1.0`, printed | The conventions 1.0.0 to 4.0.0 set, recorded in `docs/RELEASE.md` |

## Runbook — printed, not run

```bash
# 1. Pre-flight — done in this dev; repeat after merging if anything else lands
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff bugfix/2026-09-28-history-tag-push-release
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 4.0.0 are
git tag 4.1.0

# 4. Push
git push origin develop main
git push origin 4.1.0

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- the launcher starts the application;
- the splash screen and the About dialog say `Version 4.1.0`;
- `git tag` lists `4.1.0` on the merge commit on `main`, and the tag is on the remote.

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

This dev is the documentation update:

- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`'s MSI note;
- `RELEASENOTES.md`.

No other document is affected.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2511 passed**, 0 failed, 0 skipped, with
  no fix cycle.
- `strings` on the built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.App.dll` (Release) finds
  `4.1.0+fdf7d370e75ccbb04d2bf6051358798ca6d21dc7`. That is the run-branch commit this dev was cut
  from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
