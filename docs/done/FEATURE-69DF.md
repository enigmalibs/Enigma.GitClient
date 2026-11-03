# FEATURE-69DF — Release 6.15.0

**Item:** FEATURE-69DF — Release 6.15.0
**Branch:** `feature/feature-69df-release-6-15-0`
**Run:** feature/2026-11-03-final3-icons-release

## Summary

Cut **6.15.0** in 6.14.0's shape:

- the version in `Directory.Build.props` is 6.15.0. `buildmsi.ps1` reads it from there, so the installer
  it builds is now `artifacts\Enigma.GitClient.6.15.0.msi`;
- a dated `## 6.15.0 — 2026-11-03` section is on top of `RELEASENOTES.md`;
- the README callout names 6.15 and sums up 6.14 after it;
- `SECURITY.md` supports 6.15.x only.

The release carries the run's one other item, FEATURE-E219: the icon set that fills its frame. No item
was quarantined.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-69DF.md`: statuses.
- Created: `docs/done/FEATURE-69DF.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| "The new version number in the buildmsi script" | No edit to `buildmsi.ps1` / `buildmsi.bat`; the `<Version>` bump is what they build | The script has no version literal: it reads `Directory.Build.props` (FEATURE-EAB7). Its resolution code, run on its own, yields 6.15.0 (below) |
| Avalonia 12.1.5 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| `docs/RELEASE.md` | Unchanged | Its only versioned lines name the MSI that installed 6.13.0, the source of the upgrade code. They are history and stay true |
| The README callout | 6.15, then 6.14; the 6.13 sentence goes | As in 6.14.0: the newest two releases, then the older upgrade notes |
| The Windows icon cache | One sentence in *Upgrading from 6.14* | A pinned taskbar button may keep the old icon after the upgrade; the user should not take it for a failed install |

## Deviations & follow-ups

- **Before tagging:** run the Desktop suite on Linux. It was compiled but not run on this Windows
  machine, and it carries `AppIconTests` against the new icon.
- **Before tagging:** the pre-existing Windows-only failure
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` is still there (see
  `docs/done/FEATURE-0DED.md`). It passes on Linux.
- **After merging:** run `packaging\windows\buildmsi.ps1` on `main` to build
  `artifacts\Enigma.GitClient.6.15.0.msi`, then install it over 6.14.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script: it begins with `git clean -dxf`.
- Merging into `develop` and `main`, tagging `6.15.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.

## Build/test evidence

- `git tag --list 6.15.0`: empty (6.14.0 is the latest tag).
- `buildmsi.ps1`'s version resolution (its `Directory.Build.props` XPath and `^\d+\.\d+\.\d+$` check,
  copied verbatim into a scratch script) was run with Windows PowerShell 5.1 against the repository. It
  printed:
  - `version = 6.15.0`;
  - `msi = artifacts\Enigma.GitClient.6.15.0.msi`;
  - `profile = artifacts\Enigma.GitClient.6.15.0.msipkg.json`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-03:
  - only the Avalonia set has updates: Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter,
    Avalonia.Themes.Fluent, Avalonia.Headless and Avalonia.Skia, 12.1.4 → 12.1.5. Held, as above;
  - "no updates" for the other projects and packages.
- No stored format moved:
  - under `src/`, the run changed only `Assets/app.ico` and `Assets/app-256.png`;
  - `AppSettings.CurrentVersion` is 7 and `RepositoryListStore.CurrentVersion` is 1, as the notes say.
- Every path the notes quote exists (`buildmsi.ps1`, `packaging/linux/icons`, `install.sh`), and so do
  the four stored file names.
- Baseline: see `docs/done/FEATURE-E219.md`.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**. Desktop
  tests compiled.
- The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.15.0+bda055ef4ae1a6ecb327f1436cb5d9e7460bafba`.
- Core unit: 1,394 total, 1,391 passed, 2 skipped, 1 failed (the baseline's `TokenStoreTests` failure;
  nothing new).
- Core integration: 414 total, 412 passed, 2 skipped, 0 failed.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles used.

## Documentation sweep

- The release documents are this dev's subject. The README's *Publish*, *Windows installer* and
  *Install on Linux* sections and `docs/RELEASE.md` §5–§7 were re-read against 6.15.0 and are current.
