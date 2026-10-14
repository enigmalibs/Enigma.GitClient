# FEATURE-1B37 — Release 6.2.0

**Item:** FEATURE-1B37 — Release 6.2.0
**Branch:** `feature/feature-1b37-release-6-2-0`
**Run:** feature/2026-10-14-release-6-2-0

## Summary

This cuts **6.2.0**, with FEATURE-1CE1 (6.1.0) as the template. The run was cut from `develop`
(`88d5014`), which is 6.1.0 (`main`, `77ed980`) plus one merged run. 6.2.0 brings one item, `DONE`:

- **FEATURE-E228** (`develop`): the application icon joins its green commit to the line of commits by
  one rounded elbow out of the base commit, the History graph's own grammar, instead of a horizontal
  stub from the middle commit and a diagonal back. Tile, colours and dots are kept. The icon gained
  two SVG masters under `packaging/icon/`.

**Why 6.2.0:** it is the number you asked for, and it is minor under Semantic Versioning. The release
changes how the application looks, backward-compatibly: it removes nothing and changes no default or
stored format (`settings.json` stays at version 7; `git diff 6.1.0 88d5014` touches only the icon
files, its tests and the work-item records).

**What changed for the release:**

- `Directory.Build.props`: `<Version>6.2.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.2.0 — 2026-10-14` section on top, in 5.9.0's one-change minor
  shape:
  - a summary;
  - *The icon*: the elbow, what is kept, the hinted small sizes, where the icon shows, its SVG source;
  - *Upgrading from 6.1*: nothing saved changes; on Linux, the installer replaces the icon and
    refreshes the icon cache;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is now *What's new in 6.2*, with 6.1 summed up after it. 6.0's summary is
  reduced to a *Coming from 5.9?* pointer at *Upgrading from 5.9*. The 5.1.0 rename and 4.x pointers
  are kept.
- `SECURITY.md`: 6.2.x is supported; 6.1.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 6.2.0 among the releases without a profile.
- `Directory.Packages.props`: unchanged (see *Build/test evidence*).

## Runbook — printed, not run

```bash
# 1. Pre-flight — the build and every suite already ran in this dev, in Release, on Linux.
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-14-release-6-2-0
git switch main
git merge --no-ff develop

# 3. Tag 6.2.0 — bare X.Y.Z, as 1.0.0 to 6.1.0 are
git tag 6.2.0

# 4. Push
git push origin develop main
git push origin 6.2.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 6.2.0`, and show the new icon. So should the launcher.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-1B37.md`: statuses

**Created**

- `docs/done/FEATURE-1B37.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' shape | 5.9.0's: a one-paragraph summary, one section for the change, then *Upgrading*, *Dependencies*, *Version* | The latest release that carried a single change |
| The section's title | *The icon* | Every section is named after what the user sees (6.0.0's *The window's title*) |
| What the notes say about the drawing | The elbow, what is kept, the small sizes, where it shows, the SVG source | What a user notices, and what the next person to touch the icon needs; FEATURE-E228's pixel measurements stay in its own records |
| *Upgrading from 6.1* | Nothing saved changes, plus one Linux line: the installer replaces the icon and refreshes the cache; a launcher still showing the old one picks it up after logging out and in | The launcher's cache is the one place an upgrade could look as if it had not happened. `install.sh` runs `gtk-update-icon-cache` and `kbuildsycoca6` when present |
| README callout | 6.2, then *6.1 before it*; 6.0 reduced to a *Coming from 5.9?* pointer naming its two behaviour changes | 5.9.0's shape (one previous release summed up), and the callout stops growing. A 5.9 user still learns that the row buttons moved and the refresh interval changed |
| Avalonia set | Held back at 12.1.1 | 12.1.4 is out, but Enigma.Avalonia.Desktop 1.2.0 has no update and pins 12.1.1; the set moves as a whole |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |

## Deviations & follow-ups

- **None from the plan.**
- **Carried over:**
  - the Avalonia set (12.1.1 → 12.1.4) is still held back;
  - the icon's optional follow-ups (FEATURE-E228): lossless PNG recompression, and a
    `hicolor/scalable` install of the SVG master;
  - the other filter boxes have no in-box clear button (FEATURE-68B9);
  - Settings does not say when the watcher has fallen back (FEATURE-532B);
  - a remote branch delete asks no push guard (FEATURE-8E87).
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. The README's *Features* and install table say nothing the icon change made wrong.
No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 6.2.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - `Enigma.GitClient.Core` and both Core test projects: no updates;
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, and
    Avalonia.Headless and Avalonia.Skia in the tests): 12.1.1 → 12.1.4, held back;
  - nothing else, so `Directory.Packages.props` is unchanged.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**, no AVLN
  warning.
- `Enigma.GitClient.Core.UnitTests` (Release): **1183 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **363 passed, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests` (Release): **1531 passed, 0 failed**, `AppIconTests` included.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.2.0+506e60d1e8f6e1988624241fe39e4c049d6d7fe9`: the run branch's tip this dev's changes sit on.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
