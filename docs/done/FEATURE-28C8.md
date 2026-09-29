# FEATURE-28C8 — Release 4.1.1

**Item:** FEATURE-28C8 — Release 4.1.1
**Branch:** `feature/feature-28c8-release-4-1-1`
**Run:** vibe/2026-09-29-dialog-view-type-name

## Summary

The release is **4.1.1**, a PATCH. It carries one fix, BUG-7E5C: every dialog whose content is a view
showed the view's type name, a regression from 4.1.0. `4.1.1` was free (`git tag` lists up to 4.1.0).
The merges, the tag and the pushes are the user's, and are printed below.

- `Directory.Build.props` → `<Version>4.1.1</Version>`.
- `RELEASENOTES.md` — a dated `## 4.1.1 — 2026-09-29` section on top: *Fixes* (naming the regression
  from 4.1.0), *Upgrading from 4.1.0*, *Dependencies*, *Version*.
- `README.md` — the callout is *What's new in 4.1.1*, followed by what 4.1 brought.
- `docs/RELEASE.md` — the MSI paragraph records 4.1.1.
- `SECURITY.md` — no edit needed: 4.1.x is already the supported line, and 4.1.1 is in it.

## Files / modules touched

**Created:** `docs/done/FEATURE-28C8.md`

**Modified:**

- `Directory.Build.props`, `RELEASENOTES.md`, `README.md`, `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-28C8.md` — statuses

## Dependency check

`dotnet list Enigma.GitClient.slnx package --outdated` lists only the coupled Avalonia set (12.1.1 →
12.1.3, the Headless and Skia test packages included). It is held back with Enigma.Avalonia.Desktop
1.1.0. Nothing is bumped.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' contents | The dialog fix alone | BUG-6EAA, the Windows fix the plan had added, was abandoned before it was verified; it is not released |
| The callout | *What's new in 4.1.1*, then what 4.1 added | The plan's choice |

## Runbook — printed, not run

```bash
# 1. Pre-flight — the Release build and the dialog tests were run in this dev (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff vibe/2026-09-29-dialog-view-type-name
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 4.1.0 are
git tag 4.1.1

# 4. Push
git push origin develop main
git push origin 4.1.1

# 5. Publish and install (Linux)
dotnet publish src/Enigma.GitClient.App -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`: the About dialog says `Version 4.1.1` and
shows its content, and the Branches, Tags and Remotes dialogs show their pages.

## Deviations & follow-ups

- The plan (as amended when BUG-6EAA was planned) named two fixes; only BUG-7E5C is released.
- The pre-flight ran on Windows. The whole suite is red there before and after this run, for
  Windows-only reasons recorded in `docs/done/BUG-7E5C.md`. Run step 1 in full on Linux, where the
  earlier releases were verified, before tagging.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `docs/RELEASE.md`.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- The dialog tests in Release (`DialogQuestionWrapTests`, `AboutDialogTests`, `ToolDialogTests`,
  `DestructiveConfirmationTests`): **40 passed**, 0 failed.
- The Release `Enigma.GitClient.App.dll` and `Enigma.GitClient.Core.dll` carry
  `4.1.1+42b5bf2f165f97aa0f3f68f31194a507e0332c54`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
