# FEATURE-6A0E — Release 5.5.0

**Item:** FEATURE-6A0E — Release 5.5.0
**Branch:** `feature/feature-6a0e-release-5-5-0`
**Run:** feature/2026-10-05-release-5-5-0

## Summary

Cuts **5.5.0**, a MINOR release under Semantic Versioning. It ships everything this run's branch
carries beyond the `5.4.0` tag:

- **FEATURE-BB60**, *Diffs on AvaloniaEdit*, all four phases: the diff is drawn by a read-only
  AvaloniaEdit editor, with text selection, Ctrl+F search and TextMate syntax highlighting;
- **BUG-7823**: a selection dragged past the end of a diff no longer freezes the application. Both
  fixes ship, the second being the one that held (`bugfix/bug-7823-arrange-clamp`, which this run is
  cut from).

**Why 5.5.0:** it is what was asked, and SemVer agrees. The release adds backward-compatible
behaviour, and the stored settings do not change (`settings.json` stays at version 6). Two things a
5.4 user may have used go away, side-by-side wrapping and row selection. They are ways of viewing,
not data, and are called out under *Upgrading from 5.4*.

- `Directory.Build.props`: `<Version>5.5.0</Version>`.
- `RELEASENOTES.md`: a dated `## 5.5.0 — 2026-10-05` section on top, in 5.4.0's shape:
  - a summary;
  - *The diff*;
  - *Fixes*;
  - *Upgrading from 5.4*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is *What's new in 5.5*, and it keeps the 5.1.0 rename pointer and the 4.x
  one. The two diff bullets under *Features* now name syntax highlighting, text selection and copy,
  and Ctrl+F, and say that wrapping is the unified view's.
- `SECURITY.md`: 5.5.x is supported; 5.4.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 5.5.0 among the releases without a profile, re-wrapped
  to the file's width.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release, on Linux (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main. The run branch also carries
#    the FEATURE-BB60 and BUG-7823 runs, neither of which is in develop yet.
git switch develop
git merge --no-ff feature/2026-10-05-release-5-5-0
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.4.0 are
git tag 5.5.0

# 4. Push
git push origin develop main
git push origin 5.5.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`. The splash screen and About should say
`Version 5.5.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-6A0E.md`: statuses

**Created**

- `docs/done/FEATURE-6A0E.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Sections of the notes | *The diff*, *Fixes*, *Upgrading from 5.4* | 5.4.0's shape. Everything new is in one view, so one area section; the hang is a fix of this release's own feature, but a user of the BB60 build could have met it |
| What the notes claim the editor does | Only what was checked | Drag, double-click, triple-click, Shift+arrows and Ctrl+A were checked in AvaloniaEdit 12.0.0 (`SelectionMouseHandler` sets `WholeLine` on a third click; `ApplicationCommands.SelectAll` is Ctrl+A). Copy, search and the band click are covered by the suite |
| The unified view's highlighting limit | Stated in the notes | FEATURE-BB60-PHASE04 recorded it: an added line right after a removed one can stay plain. Saying so beats a bug report |
| Where the wrap setting is | *Settings → Diff → Wrap long lines in the unified view* | Read from `SettingsPageView.axaml`: the toggle sits in the *Diff* expander, relabelled by FEATURE-BB60 |
| "Going back to 5.4 keeps everything" | Stated | Nothing under `src/Enigma.GitClient.Core` changed since `5.4.0`; `AppSettings.CurrentVersion` is still 6 |
| Licences of the new runtime packages | Recorded in *Dependencies* | Read from the nuspecs: AvaloniaEdit, AvaloniaEdit.TextMate, TextMateSharp 2.0.3, TextMateSharp.Grammars 2.0.3 and onigwrap 1.0.10 are MIT. onigwrap's `THIRD-PARTY-NOTICES.TXT` carries Oniguruma's BSD licence. All allow redistribution |
| Dependency bumps | None | Only the coupled Avalonia set (12.1.1 → 12.1.3) moved, held back with Enigma.Avalonia.Desktop 1.2.0. AvaloniaEdit 12.0.0 is the latest |
| Integration path in the runbook | The run branch into `develop` | It carries both earlier runs, so one merge brings all three; merging them separately would add nothing |
| MSI profile | None, as for every release so far | The packaging is the Linux installer (`docs/RELEASE.md`) |

## Deviations & follow-ups

- None from the plan.
- Syntax highlighting, selection and search were exercised by the headless suite, not by hand. Before
  tagging, they are worth a click:
  - open a `.cs` diff in both renderings and both themes;
  - drag a selection to the bottom-right of a long diff;
  - press Ctrl+F.
- `bugfix/bug-7823-arrange-clamp`, which this run is cut from, was never merged into its own run
  branch `bugfix/2026-10-05-diff-drag-hang`. This run's branch carries it, so merging this run is
  enough.
- The Avalonia set (12.1.1 → 12.1.3) is still held back.
- The unified view's syntax highlighting can miss an added line right after a removed one
  (FEATURE-BB60-PHASE04's follow-up: tokenize the old and new sides apart).
- Reporting the AvaloniaEdit offset clamp upstream is still open (BUG-7823's follow-up).
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update:

- the notes;
- the README callout and the diff bullets;
- `SECURITY.md`;
- `docs/RELEASE.md`.

No `CLAUDE.md` or `AGENTS.md` exists. No other prose doc names the current version.

## Build/test evidence

- `git tag --list 5.5.0` was empty.
- `dotnet list package --outdated`: only the Avalonia set moved, 12.1.1 → 12.1.3. That is Avalonia,
  Avalonia.Desktop, Avalonia.Fonts.Inter and Avalonia.Themes.Fluent, plus Avalonia.Headless and
  Avalonia.Skia in the tests. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: 2814 total, 2814 passed, 0 failed,
  0 skipped — the whole suite, Desktop included, on Linux.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.5.0+5de37f01a7e4368839d3880add3dcb2c7edfbc22`, the run-branch commit this dev was cut from.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
