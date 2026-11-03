# FEATURE-AA46 — Release 6.14.0

**Item:** FEATURE-AA46 — Release 6.14.0
**Branch:** `feature/feature-aa46-release-6-14-0`
**Run:** feature/2026-11-03-icons-msi-release

## Summary

Cut **6.14.0** in 6.13.0's shape:

- the version in `Directory.Build.props` is 6.14.0;
- a dated `## 6.14.0 — 2026-11-03` section is on top of `RELEASENOTES.md`;
- the README callout names 6.14 and sums up 6.13 after it;
- `SECURITY.md` supports 6.14.x only.

The release carries the run's two `DONE` items:

- FEATURE-35EB: the icon without its dark tile;
- FEATURE-EAB7: `buildmsi.ps1` / `buildmsi.bat` and the vendored Enigma.Msi worker.

No item was quarantined.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-AA46.md`: statuses.
- Created: `docs/done/FEATURE-AA46.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.5 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| The Enigma.Msi worker in *Dependencies* | Named, as a build tool outside the application | It is new in the repository, but nothing in the application depends on it |
| `docs/RELEASE.md` | Unchanged | FEATURE-EAB7 already rewrote its installer sections. Its only 6.13.0 mentions are about the MSI that installed 6.13.0, and they stay true |
| The README callout | 6.14, then 6.13; the 6.12 sentence goes | As in 6.13.0: the newest two releases, then the older upgrade notes |

## Deviations & follow-ups

- **Before tagging:** run the Desktop suite on Linux. It was compiled but not run on this Windows
  machine, and it carries `AppIconTests` against the new icon and the new
  `AssemblyNameConsistencyTests` fact.
- **Before tagging:** the pre-existing Windows-only failure
  `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` is still there (see
  `docs/done/FEATURE-0DED.md`); it passes on Linux.
- **After merging:** run `packaging\windows\buildmsi.ps1` on `main` to build
  `artifacts\Enigma.GitClient.6.14.0.msi`, then install it over 6.13.0 (`docs/RELEASE.md` §6–§7). The
  run built a 6.13.0 MSI in a scratch clone only, and installed nothing.
- Merging into `develop` and `main`, tagging `6.14.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.

## Build/test evidence

- `git tag --list 6.14.0`: empty (6.13.0 is the latest tag).
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-03:
  - only the Avalonia set has updates: Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter,
    Avalonia.Themes.Fluent, Avalonia.Headless and Avalonia.Skia, 12.1.4 → 12.1.5. Held, as above;
  - "no updates" for the other projects and packages.
- `git diff 8a526e7 HEAD -G CurrentVersion`: empty, so no stored format moved:
  - `AppSettings.CurrentVersion` is 7 and `RepositoryListStore.CurrentVersion` is 1, as the notes say;
  - under `src/`, the run changed only `Assets/app.ico` and `Assets/app-256.png`.
- Every path the notes quote exists (`buildmsi.ps1`, `buildmsi.bat`, `publish.ps1`,
  `Enigma.Msi.Worker/`, `packaging/linux/icons`, `install.sh`, `WindowsInstallerIdentityTests`), and so
  do the four stored file names.
- Baseline: see `docs/done/FEATURE-35EB.md`.
- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**. Desktop tests compiled.
- The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.14.0+7dcb95295ce6f9ff62b5abf7130d42176aab3383`.
- Core unit: 1,394 total, 1,391 passed, 2 skipped, 1 failed (the baseline's `TokenStoreTests` failure;
  nothing new).
- Core integration: 414 total, 412 passed, 2 skipped, 0 failed.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`, or installed an MSI.
- Fix budget: 0 cycles used.

## Documentation sweep

- The release documents are this dev's subject. The README's *Publish*, *Windows installer* and
  *Install on Linux* sections and `docs/RELEASE.md` §5–§7 were re-read against 6.14.0 and are current.
