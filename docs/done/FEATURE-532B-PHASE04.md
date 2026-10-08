# FEATURE-532B-PHASE04 — Setting and 60 s default

**Item:** FEATURE-532B — File-system watcher for instant refresh
**Phase:** PHASE04 — Setting and 60 s default
**Branch:** `feature/feature-532b-phase04-setting-default`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

The watcher gets its switch, and the automatic refresh a default that fits what it now does.

- **Settings, *Git* card:** a *Watch the repository for changes* toggle
  (`SettingsPageViewModel.WatchFileSystem`), with a sentence underneath that says:
  - what it does;
  - that the automatic refresh still fetches;
  - when to turn it off: a network share or a WSL `/mnt` drive, which report no changes, or a tree
    too large for the system's file-watch limits.

  The watcher follows the setting at once (PHASE01).
- **The automatic refresh defaults to 60 s** (it was 15 s).
  - **Migration:** `settings.json` moves to schema **version 7**. A file written by version 1 to 6
    whose interval is exactly 15 (`AppSettings.LegacyAutoRefreshSeconds`, a value nobody chose) moves
    to 60. Any other value is kept: 0 stays off, and 30 stays 30. From version 7 on, 15 is a
    preference like any other.
  - **The interval's sentence** in Settings now says that while the repository is watched, what
    changes on this machine shows at once, so the interval mostly sets how often the remote is
    fetched.
  - **The card's description** says "how the repository is kept up to date".
- **README:** a *Features* line for the watcher and the 60 s refresh, and the two new preferences in
  *Preferences that stick*.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs`: `CurrentVersion = 7`,
  `LegacyAutoRefreshSeconds`, the 60 s default and its documentation.
- `src/Enigma.GitClient.Core/Configuration/SettingsService.cs`: the version 7 migration and its
  documentation.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/SettingsPageViewModel.cs`: `WatchFileSystem`.
- `src/Enigma.GitClient.Desktop/Views/Pages/SettingsPageView.axaml`: the toggle, the card's description
  and the interval's sentence.
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs`:
  - the 60 s default;
  - watching on by default;
  - a file without the keys;
  - 15 moved to 60 at version 6;
  - 30, 0 and a version 7 file's 15 kept;
  - the version 1 file going through every migration.
- `tests/Enigma.GitClient.Desktop.UnitTests/SettingsPageTests.cs`: the 60 s default, the toggle on by
  default and writing the setting, the toggle in the *Git* card.
- `tests/Enigma.GitClient.Desktop.UnitTests/AutoRefreshTests.cs`: the tick test names its 15 s
  interval itself rather than relying on the default.
- `README.md`: as above.
- `docs/roadmap.md`, `docs/plan/FEATURE-532B.md`: statuses. The item is `DONE` with this phase.

**Created**

- `docs/done/FEATURE-532B-PHASE04.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the toggle sits | Right under the interval, in the *Git* card | The two settings decide together how the repository is kept up to date |
| A version 6 file's 0 (off) | Kept | Off is a choice, not the old default |
| The tick test | Sets 15 s itself | It tests "the interval the settings name", not the default |
| A status line for the watcher | Not added | The plan leaves it out of scope. A failed watch is logged once (PHASE01) |

## Deviations & follow-ups

- None from the plan.
- **Follow-up:** Settings could say when the watcher has fallen back (inotify limit reached). The
  plan left that out of scope.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

`README.md`, as planned: the watcher in *Features*, and the refresh interval and the toggle among the
preferences. `RELEASENOTES.md` comes with FEATURE-AE9F. No `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 2979 total, 2978 passed, 1 skipped,
  0 failed.
- Fix budget: 0 cycles.
