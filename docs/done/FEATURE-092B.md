# FEATURE-092B — Release 6.18.0

**Item:** FEATURE-092B — Release 6.18.0
**Branch:** `feature/feature-092b-release-6-18-0`
**Run:** feature/2026-11-06-release-6-18-0

## Summary

Cut **6.18.0** in 6.17.0's shape:

- the version in `Directory.Build.props` is 6.18.0. `buildmsi.ps1` reads it from there, so the
  installer it builds is now `artifacts\Enigma.GitClient.6.18.0.msi`;
- a dated `## 6.18.0 — 2026-11-06` section is on top of `RELEASENOTES.md`;
- the README callout names 6.18 and sums up 6.17 after it. The README also describes the tile's menu
  and icon, and lists `repository-icons/` among the things kept;
- `SECURITY.md` supports 6.18.x only.

The release carries everything built since the `6.17.0` tag, all `DONE`:

- FEATURE-4F5C (PHASE01–04): a repository tile's menu, a repository's own icon, a more legible tile;
- FEATURE-0CDE: the History's search in a bar of its own, with a *Search* toggle and Ctrl+F;
- BUG-553F: a disabled menu item's glyph greys like its title.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-092B.md`: statuses.
- Created: `docs/done/FEATURE-092B.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.5 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| `docs/RELEASE.md` | Unchanged | It names no current version: `1.1.0` is an example, and 6.13.0 is the MSI's history |
| The new icon folder in *Upgrading from 6.17* | Named, with "6.17 does not read it" | It is the one new thing written in the configuration directory; going back is still safe |
| The moved search box in *Upgrading from 6.17* | One line: Ctrl+F or the *Search* toggle | A user who looked for the box at the command bar's right end would not find it |
| The README's start-window bullet | One clause appended: the tile's menu and *Choose an icon…* | PHASE04 left the description to this release; the bullet is where the start window is described |
| The README's search bullet | Unchanged | It is still true; where the box sits is in the notes |
| Disabled glyphs, where | "Every menu of the application, the History's line and badge menus among them" | BUG-553F's fix is one style on every `ei:Icon.menu` |

## Deviations & follow-ups

- **After merging:** run `packaging\windows\buildmsi.ps1` on `main`, on Windows, to build
  `artifacts\Enigma.GitClient.6.18.0.msi`, then install it over 6.17.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script: it starts with `git clean -dxf`, and this machine is Linux.
- Merging into `develop` and `main`, tagging `6.18.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.
- The README's `[RELEASENOTES.md](RELEASENOTES.md)` and `[LICENSE.md](LICENSE.md)` are relative links.
  That is fine here: the application is not packed, so its README is never shown on nuget.org.
  Pre-existing; left alone.
- The pre-existing follow-ups from the features stand: Escape leaving the diffs focuses nothing
  (`docs/done/FEATURE-0CDE.md`); a forgotten repository's icon stays in `repository-icons`
  (`docs/done/FEATURE-4F5C-PHASE03.md`).

## Build/test evidence

- `git tag --list 6.18.0`: empty (6.17.0 is the latest tag).
- **Baseline**, the run branch at `25c757f` (the starting `HEAD` `5610c3b` plus the planning commit),
  taken once for the run, in Release, on Linux:
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --solution Enigma.GitClient.slnx -c Release`: 3675 run, 3672 passed, 0 failed,
    3 skipped.
  - Clean: the strict rule applies (zero warnings, every test passing). No pre-existing failure.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**; the
    Debug build too.
  - `dotnet test --solution Enigma.GitClient.slnx -c Release`: 3675 run, 3672 passed, **0 failed**,
    3 skipped.
  - The version-reading classes on their own, all passed:
    - Core: `ProductInformationTests`, `HostHttpTests`, `DisplayNameConsistencyTests`, 28/28;
    - Desktop: `AboutDialogTests`, 17/17.
  - The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` (Release) carry
    `6.18.0+25c757fd09b28b5ff2470f82d6f5034fd98ddb67`.
- `buildmsi.ps1`'s version lookup, replayed read-only with its XPath (`/Project/PropertyGroup/Version`)
  and pattern (`^\d+\.\d+\.\d+$`): `version = 6.18.0`, `msi = artifacts\Enigma.GitClient.6.18.0.msi`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-06: only the Avalonia set
  (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Avalonia.Headless,
  Avalonia.Skia) 12.1.4 → 12.1.5; held, as above. Every other project: no updates.
- **The notes were checked against the files:**
  - stored formats: `AppSettings.CurrentVersion` 7, `RepositoryListStore.CurrentVersion` 1,
    `HostAccountService.CurrentVersion` 2, `TokenStore.CurrentVersion` 1, as in 6.17.0;
    `ListedRepository.IconFile` is `[JsonIgnore]`, so `repository-lists.json` is unchanged;
  - the icon store: `RepositoryIconStore.DirectoryName` `repository-icons`, `MaxImageBytes` 10 MB,
    `MaxSvgBytes` 1 MB, `Extensions` PNG, JPG/JPEG, BMP, GIF, ICO, WebP, SVG;
  - texts: the tile menu's eight items (`RepositoriesPageView.axaml`), the picker's title and the
    error bar's "The icon was not changed" (`RepositoriesPageViewModel`), the *Search* toggle and its
    tip (`HistoryPageView.axaml`);
  - paths: `packaging/windows/buildmsi.ps1`, `packaging/linux/install.sh`; Enigma.Msi 1.7.0 and
    WixSharp_wix4 2.14.2, unchanged since 6.17.0.
- No Core source changed since `6.17.0`: no public Core API moves.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles.

## Documentation sweep

- The release documents are this dev's subject; the README edits above are its own. There is no
  `CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING.md` or `CHANGELOG.md`. No further edit.
