# FEATURE-A57F — Release 5.8.0

**Item:** FEATURE-A57F — Release 5.8.0
**Branch:** `feature/feature-a57f-release-5-8-0`
**Run:** vibe/2026-10-08-file-panel-about-release

## Summary

This cuts **5.8.0**. It ships what this run's branch carries beyond 5.7.0: four items, all done, none
quarantined.

- **BUG-1B14**: a folder in the files panel is never selected.
  - A click folds or unfolds it, and the selected file keeps its diff.
  - One click on a file always selects it. The hidden list no longer writes `null` over the tree's
    choice.
- **BUG-D0D9**: the diff area's messages (binary, select a file, rename-only, …) are shown alone,
  without the empty editor drawn around them.
- **FEATURE-E2D2**: *Expand all* / *Collapse all* buttons in the files panel's header, in tree mode.
- **FEATURE-62B7**: the About dialog is down to the icon, name, version, build, copyright and
  *Close*. The title, the Info icon and *BUILT WITH* are gone.

**Why 5.8.0:** the expand and collapse buttons are new, backward-compatible behaviour, which makes
this a minor release under SemVer. It is also the number you asked for. No Core source changed since
5.7.0, and `settings.json` stays at version 6.

**What changed for the release:**

- `Directory.Build.props`: `<Version>5.8.0</Version>`.
- `Directory.Packages.props`: Microsoft.Testing.Extensions.CodeCoverage 18.11.2 → 18.12.0, a
  non-coupled, test-only bump.
- `RELEASENOTES.md`: a dated `## 5.8.0 — 2026-10-08` section on top, in 5.7.0's and 5.6.0's shape:
  - a summary;
  - *The files panel*;
  - *The About dialog*;
  - *Fixes*;
  - *Upgrading from 5.7*;
  - *Dependencies*;
  - *Version*.
- `README.md`:
  - the callout is now *What's new in 5.8*, and keeps the 5.1.0 rename pointer and the 4.x one. The
    5.6 line went: 5.6.0 and 5.7.0 are both tagged now;
  - the *Features* bullet on the changed-files panel mentions folders folding on a click and the two
    buttons.
- `SECURITY.md`: 5.8.x is supported; 5.7.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.8.0 among the releases without a profile.

## Runbook — printed, not run

```bash
# 0. Before tagging: run the Desktop suite where it works (Linux); it was not run this session
dotnet test --solution Enigma.GitClient.slnx -c Release

# 1. Pre-flight — already run in this dev, in Release, on Windows (Core suites only; see below)
dotnet build Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff vibe/2026-10-08-file-panel-about-release
git switch main
git merge --no-ff develop

# 3. Tag 5.8.0 — bare X.Y.Z, as 1.0.0 to 5.7.0 are
git tag 5.8.0

# 4. Push
git push origin develop main
git push origin 5.8.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 5.8.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `Directory.Packages.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-A57F.md`: statuses

**Created**

- `docs/done/FEATURE-A57F.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The README callout's older lines | Keep the 5.1.0 rename and 4.x pointers; drop the 5.6 line | 5.6.0 and 5.7.0 are tagged, so someone upgrading from 5.7 gets 5.8 alone |
| The README *Features* bullet | Updated for folders and the two buttons | It describes the panel's clicks, which this release changed |
| The CodeCoverage bump | Applied | Non-coupled, test-only, a minor bump; the house rule for a release |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |
| The Release pre-flight | Built in place, `-c Release --no-incremental` | No test host was holding the Release outputs |

## Deviations & follow-ups

- **The Desktop suite was not run** (your instruction for this session), and it is the suite that
  covers everything this release changes. Run it on Linux before tagging (runbook step 0). In
  particular:
  - `FolderLineTests` (its six folder tests ran green before your instruction);
  - `ChangedFilesPanelTests`;
  - `FileListToggleTests`;
  - `DiffViewerTests`;
  - `AboutDialogTests`.
- **Worth a click before tagging**, the manual checks in each item's completion doc:
  - folders: click, double-click, the arrow keys, a root file then a nested one;
  - a binary file's diff, in both shapes;
  - *Expand all* / *Collapse all* with a nested file selected;
  - About, opened from the toolbar, from Settings and from the start window.
- Follow-ups carried from the items:
  - a THIRD-PARTY-NOTICES file (FEATURE-62B7);
  - the Avalonia set (12.1.1 → 12.1.3) is still held back.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout and *Features* bullet,
`SECURITY.md` and `docs/RELEASE.md`. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 5.8.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - the Avalonia set has 12.1.1 → 12.1.3 for Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter and
    Avalonia.Themes.Fluent, plus Avalonia.Headless and Avalonia.Skia in the tests. It is held back
    with Enigma.Avalonia.Desktop 1.2.0, which has no update;
  - Microsoft.Testing.Extensions.CodeCoverage 18.11.2 → 18.12.0 was **applied**;
  - nothing else moved.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.8.0+e2a4a44f0556efa63f2bfef719983fdc20f87791`, the run-branch commit this dev was cut from.
- `Enigma.GitClient.Core.UnitTests`, Release: 1165 total, 0 failed, 1 skipped (the AtomicFile race
  excluded on Windows, BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`, Release: 358 total, 0 failed, 2 skipped. These ran with
  CodeCoverage 18.12.0.
- `Enigma.GitClient.Desktop.UnitTests`: **not run**, at your instruction.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
