# FEATURE-6352 — Release 6.3.0

**Item:** FEATURE-6352 — Release 6.3.0
**Branch:** `feature/feature-6352-release-6-3-0`
**Run:** feature/2026-10-15-create-identity-release

## Summary

This cuts **6.3.0**, with FEATURE-1B37 (6.2.0) and FEATURE-1CE1 (6.1.0) as the templates. The run was
cut from `feature/2026-10-15-worktrees-toolbar` (`aed2a17`), which is `develop` (`d0962e4`, the 6.2.0
content) plus one unmerged run. 6.3.0 brings three items, all `DONE`:

- **FEATURE-19A5** (`feature/2026-10-15-worktrees-toolbar`): *Create worktree…* on a commit's line, a
  branch's badge and a branch's line in the branches dialog — a new branch, checked out in a folder
  next to the repository (`<repository>.worktree.<branch>`), opened in a new window.
- **FEATURE-70F3** (`feature/2026-10-15-worktrees-toolbar`): the repository window's toolbar no longer
  has the *Open another window* button; the start window's *Open in a new window* stays.
- **FEATURE-B979** (this run): *Create a repository* offers the global identity, as a clone does.

**Why 6.3.0:** it is the number you asked for, and it is minor under Semantic Versioning. It adds
backward-compatible behaviour and changes no default or stored format (`settings.json` stays at
version 7; `git diff 6.2.0 HEAD -- src/Enigma.GitClient.Core/Configuration` is empty). The one removed
toolbar button was a shortcut whose function stays on the start window; the notes say so.

**What changed for the release:**

- `Directory.Build.props`: `<Version>6.3.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.3.0 — 2026-10-15` section on top, in 6.1.0's several-change minor
  shape:
  - a summary list;
  - *Worktrees*: the three places, the dialog (branch, folder next to the repository, new window),
    what follows;
  - *The new repository's identity*: the question, *Use it* / *Not now*, when nothing is asked, a
    failed write;
  - *The toolbar*: the button gone, where its function is;
  - *Upgrading from 6.2*;
  - *Dependencies*;
  - *Version*.
- `README.md`: the callout is now *What's new in 6.3*, with 6.2 summed up after it. 6.1's summary is
  dropped. The 5.9, 5.1.0 and 4.x pointers are kept.
- `SECURITY.md`: 6.3.x is supported; 6.2.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 6.3.0 among the releases without a profile (and is
  re-wrapped).
- `Directory.Packages.props`: unchanged (see *Build/test evidence*).

## Runbook — printed, not run

```bash
# 0. The Desktop suite was compiled, not run, on Windows: run it on Linux first.
dotnet test --project tests/Enigma.GitClient.Desktop.UnitTests -c Release

# 1. Pre-flight
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: this run branch (it carries feature/2026-10-15-worktrees-toolbar) into develop,
#    then develop into main
git switch develop
git merge --no-ff feature/2026-10-15-create-identity-release
git switch main
git merge --no-ff develop

# 3. Tag 6.3.0 — bare X.Y.Z, as 1.0.0 to 6.2.0 are
git tag 6.3.0

# 4. Push
git push origin develop main
git push origin 6.3.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 6.3.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-6352.md`: statuses

**Created**

- `docs/done/FEATURE-6352.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The notes' shape | 6.1.0's: a summary list, one section per area, then *Upgrading*, *Dependencies*, *Version* | Three changes; the latest several-change minor release |
| Section titles | *Worktrees*, *The new repository's identity*, *The toolbar* | Every section is named after what the user sees |
| Section order | Worktrees, identity, toolbar | The largest change first, the removal last, as the summary list reads |
| The removed button | Said in *The toolbar* and pointed at from *Upgrading from 6.2* | The one thing a 6.2 user could look for and not find |
| README callout | 6.3, then *6.2 before it*; 6.1's summary dropped; the 5.9, 5.1.0, 4.x pointers kept | 6.2.0's shape: one previous release summed up, and the callout stops growing |
| README *Features* | Untouched | FEATURE-19A5 added its *Worktrees* bullet; FEATURE-70F3 and FEATURE-B979 make no feature line wrong (FEATURE-B979 updated *Where your things are kept*) |
| Avalonia set | Held back at 12.1.1 | 12.1.4 is out, but Enigma.Avalonia.Desktop 1.2.0 has no update and pins 12.1.1; the set moves as a whole |
| MSI profile | Not generated | The convention every release so far has set (`docs/RELEASE.md`, *Why there is no MSI profile*) |
| The Desktop suite | First step of the runbook, on Linux | It is compiled, not run, on this Windows machine |

## Deviations & follow-ups

- **None from the plan.**
- **Carried over:**
  - the Avalonia set (12.1.1 → 12.1.4) is still held back;
  - listing and removing worktrees, and saying when a branch is checked out in another worktree
    (FEATURE-19A5);
  - the icon's optional follow-ups (FEATURE-E228): lossless PNG recompression, and a
    `hicolor/scalable` install of the SVG master;
  - the other filter boxes have no in-box clear button (FEATURE-68B9);
  - Settings does not say when the watcher has fallen back (FEATURE-532B);
  - a remote branch delete asks no push guard (FEATURE-8E87).
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. Nothing else the release makes wrong. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 6.3.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - `Enigma.GitClient.Core` and both Core test projects: no updates;
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, and
    Avalonia.Headless and Avalonia.Skia in the tests): 12.1.1 → 12.1.4, held back;
  - nothing else, so `Directory.Packages.props` is unchanged.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**, no AVLN
  warning.
- `Enigma.GitClient.Core.UnitTests` (Release): **1220 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **372 passed, 2 skipped, 0 failed** (3 min 49 s;
  the two skips are Windows-only, e.g. file modes).
- `Enigma.GitClient.Desktop.UnitTests` (Release): **compiled, not run**, under the standing instruction
  never to run the Desktop suite on this Windows machine. The runbook runs it on Linux first.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.3.0+19bfe78abfd920e40615a8e24c0ddf9724aca3f1`: the run branch's tip this dev's changes sit on.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
