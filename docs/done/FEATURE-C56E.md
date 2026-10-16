# FEATURE-C56E — Release 6.4.0

**Item:** FEATURE-C56E — Release 6.4.0
**Branch:** `feature/feature-c56e-release-6-4-0`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

This cuts **6.4.0**, with FEATURE-6352 (6.3.0) as the template. The run was cut from `develop`
(`751fe5a`), which holds the 6.3.0 content and this release's plans. 6.4.0 brings two items, both
`DONE` on this run branch:

- **FEATURE-C3F3:**
  - worktrees removed from the application, with or without their branch;
  - the *Worktrees* tool dialog;
  - the tree mark on branches checked out in another working tree, and the repository strip's
    worktree mark;
  - the up-front refusals of deleting or checking out such a branch.
- **FEATURE-CA9D:** the windows open on the screen in use:
  - the hand-over between the start and the repository windows;
  - a fresh start on the last screen;
  - another instance on its caller's screen.

**Why 6.4.0:** it is minor under Semantic Versioning. It adds backward-compatible behaviour and
removes nothing. `settings.json` stays at version 7: `lastScreen` is a remembered value that an older
version ignores.

**What changed for the release:**

- `Directory.Build.props`: `<Version>6.4.0</Version>`.
- `RELEASENOTES.md`: a dated `## 6.4.0 — 2026-10-16` section on top, in 6.3.0's several-change minor
  shape:
  - a summary list;
  - *Worktrees*: removal and what it asks, opening, the dialog, the tree mark, the refusals;
  - *Windows and screens*: the hand-over, a fresh start, another instance, what is unchanged, Linux;
  - *Upgrading from 6.3*, *Dependencies*, *Version*.
- `README.md`: the callout is now *What's new in 6.4*, with 6.3 summed up after it. 6.2's summary is
  dropped, and the 5.9, 5.1.0 and 4.x pointers are kept.
- `SECURITY.md`: 6.4.x is supported; 6.3.x no longer is.
- `docs/RELEASE.md`: the MSI paragraph lists 6.4.0 among the releases without a profile, and is
  re-wrapped.
- `Directory.Packages.props`: unchanged (see *Build/test evidence*).

## Runbook — printed, not run

```bash
# 0. Before tagging, on Linux: the Desktop suite was compiled, not run, on this Windows machine.
dotnet test --project tests/Enigma.GitClient.Desktop.UnitTests -c Release
#    And by hand, on Windows with two screens (one at 100 %, one at 150 %):
#    - start window on screen 2 → open a repository → it maximises on screen 2;
#      Close repository → the start window comes back centred on screen 2;
#    - quit on screen 2, start from the taskbar → splash and start window on screen 2;
#      unplug screen 2, start → the primary screen;
#    - a tile's "Open in a new window" from screen 2 → the new instance on screen 2;
#      the same from a worktree;
#    - remove a worktree from a branch's menu and from the Worktrees dialog, with and without
#      its branch, and with an uncommitted change in it.

# 1. Pre-flight
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: this run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e
git switch main
git merge --no-ff develop

# 3. Tag 6.4.0 — bare X.Y.Z, as 1.0.0 to 6.3.0 are
git tag 6.4.0

# 4. Push
git push origin develop main
git push origin 6.4.0

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then follow *Post-release verification* in `docs/RELEASE.md`. The splash screen and About should say
`Version 6.4.0`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `SECURITY.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`, `docs/plan/FEATURE-C56E.md`: statuses

**Created**

- `docs/done/FEATURE-C56E.md`

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The notes' shape | 6.3.0's: a summary list, one section per area, then *Upgrading*, *Dependencies*, *Version* | The plan's sections; two items, each large enough for its own section | One flat list |
| Section order | *Worktrees*, then *Windows and screens* | The plan's order, and the larger change first | Alphabetical |
| `lastScreen` in *Upgrading* | Named, with "an older version ignores it" | It is the one new thing in the settings file, and the reason going back to 6.3 is safe | Not mentioned |
| `--screen` in the notes | Named, with "a command line without it opens as before" | Someone starting the application from a script may see it in a process list | Left out |
| README callout | 6.4, then *6.3 before it*; 6.2's summary dropped; the 5.9, 5.1.0 and 4.x pointers kept | 6.3.0's shape: one previous release summed up, so the callout stops growing | Keeping 6.2 too |
| README *Features* | Untouched here | FEATURE-C3F3-PHASE05 already updated them; FEATURE-CA9D makes no feature line wrong | Re-editing them |
| Avalonia set | Held back at 12.1.1 | 12.1.4 is out, but Enigma.Avalonia.Desktop 1.2.0 has no update and pins 12.1.1; the set moves as a whole | Bumping the set alone |
| MSI profile | Not generated | The convention every release so far has kept (`docs/RELEASE.md`, *Why there is no MSI profile*) | Generating the first profile |
| The Desktop suite and the manual checks | Step 0 of the runbook, before tagging | The Desktop suite is compiled, not run, on this Windows machine; the screen checks need two real screens | Leaving them out |

## Deviations & follow-ups

- **None from the plan.**
- **One flaky Core integration run.** The first integration run in this dev reported 1 failure out of
  388, and its name was not captured. Five full runs after it, with no code changed in between,
  passed. No fix was attempted, so this is not a fix cycle. If it recurs, the run's log names it.
- **Carried over:**
  - the Avalonia set (12.1.1 → 12.1.4) is still held back;
  - `SettingsService.Dispose` releases its write gate under an in-flight write (FEATURE-C3F3-PHASE03),
    and the other `SettingsServiceTests` share the teardown race fixed there;
  - pulling a non-current branch that another worktree has checked out is refused by git with its own
    message (FEATURE-C3F3-PHASE03);
  - the icon's optional follow-ups (FEATURE-E228);
  - the other filter boxes have no in-box clear button (FEATURE-68B9);
  - Settings does not say when the watcher has fallen back (FEATURE-532B);
  - a remote branch delete asks no push guard (FEATURE-8E87).
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

This dev is the documentation update: the notes, the README callout, `SECURITY.md` and
`docs/RELEASE.md`. Nothing else is made wrong by the release. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `git tag --list 6.4.0` was empty.
- `dotnet list Enigma.GitClient.slnx package --outdated`:
  - `Enigma.GitClient.Core` and both Core test projects: no updates;
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, and
    Avalonia.Headless and Avalonia.Skia in the tests): 12.1.1 → 12.1.4, held back;
  - nothing else, so `Directory.Packages.props` is unchanged.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**, no AVLN
  warning.
- `Enigma.GitClient.Core.UnitTests` (Release): **1306 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **386 passed, 2 skipped, 0 failed**, in five
  consecutive runs (see the flaky first run above). The two skips are Windows-only, such as file modes.
- `Enigma.GitClient.Desktop.UnitTests` (Release): **compiled, not run**, under the standing instruction
  never to run the Desktop suite on this Windows machine. The runbook runs it on Linux first.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `6.4.0+b2465c39f5583a5646c2223afe78c9eac4146028` (file version `6.4.0.0`): the run branch's tip this
  dev's changes sit on.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles.
