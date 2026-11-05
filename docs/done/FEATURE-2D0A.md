# FEATURE-2D0A — Release 6.16.0

**Item:** FEATURE-2D0A — Release 6.16.0
**Branch:** `feature/feature-2d0a-release-6-16-0`
**Run:** feature/2026-11-05-history-badges-release

## Summary

Cut **6.16.0** in 6.15.0's shape:

- the version in `Directory.Build.props` is 6.16.0. `buildmsi.ps1` reads it from there, so the
  installer it builds is now `artifacts\Enigma.GitClient.6.16.0.msi`;
- a dated `## 6.16.0 — 2026-11-05` section is on top of `RELEASENOTES.md`;
- the README callout names 6.16 and sums up 6.15 after it;
- `SECURITY.md` supports 6.16.x only.

The release carries the run's four other items, all `DONE`, none quarantined:

- FEATURE-D7D5: stacked, ordered badges, the line on top;
- FEATURE-F452: the line's items on badge menus;
- FEATURE-70CB: delete a branch and its remote at once;
- FEATURE-0123: the cloud on pushed tags.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-2D0A.md`: statuses.
- Created: `docs/done/FEATURE-2D0A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.5 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| `docs/RELEASE.md` | Unchanged | Its only versioned lines name the MSI that installed 6.13.0: history, still true |
| The README callout | 6.16, then 6.15; 6.14's sentence goes | As in 6.15.0: the newest two releases, then the older upgrade notes |
| The extra network read | One line in *Upgrading from 6.15* | `ls-remote` after each fetch is a new round trip; whoever has a slow remote should know where it comes from |
| The Core additions | Named in *Version* | `ITagService.ListRemoteAsync` and `RemoteTagParser` are new public members: an addition, so a minor release |

## Deviations & follow-ups

- **After merging:** run `packaging\windows\buildmsi.ps1` on `main`, on Windows, to build
  `artifacts\Enigma.GitClient.6.16.0.msi`, then install it over 6.15.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script: it starts with `git clean -dxf`.
- Merging into `develop` and `main`, tagging `6.16.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.
- The flaky Desktop tests noted in `docs/done/FEATURE-D7D5-PHASE01.md` and
  `docs/done/FEATURE-0123-PHASE01.md` did not fail in this dev's runs.

## Build/test evidence

- `git tag --list 6.16.0`: empty (6.15.0 is the latest tag).
- `buildmsi.ps1`'s version resolution, re-done with the same XPath (`/Project/PropertyGroup/Version`)
  and pattern (`^\d+\.\d+\.\d+$`) in a scratch Python one-liner (there is no PowerShell on this Linux
  machine): `version = 6.16.0`, `msi = artifacts\Enigma.GitClient.6.16.0.msi`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-05: only the Avalonia set (Avalonia,
  Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Avalonia.Headless, Avalonia.Skia)
  12.1.4 → 12.1.5; held, as above. Every other project: no updates.
- No stored format moved: `AppSettings.CurrentVersion` 7, `RepositoryListStore.CurrentVersion` 1,
  `HostAccountService.CurrentVersion` 2, `TokenStore.CurrentVersion` 1, as before the run.
- Every path the notes quote exists (`packaging/windows/buildmsi.ps1`, `packaging/linux/install.sh`).
- Baseline: see `docs/done/FEATURE-D7D5-PHASE01.md`.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**; the Debug
  build too.
- The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.16.0+d81ec69cd412b88087ded6a28547dd7a438479bb`.
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: 3,626 total, 3,623 passed, 3 skipped,
  **0 failed**. On this Linux machine the Desktop suite runs.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles.

## Documentation sweep

- The release documents are this dev's subject. The README's feature list was re-read against the
  run: line 25 ("ref badges") and the badge lines stay true.
