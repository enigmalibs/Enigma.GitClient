# FEATURE-3988 — The name Enigma Git Client

**Item:** FEATURE-3988 — The name Enigma Git Client
**Branch:** `feature/feature-3988-enigma-git-client-name`
**Run:** feature/2026-09-30-rename-and-release

## Summary

The application's name in words is now **"Enigma Git Client"** (it was "Enigma git client")
everywhere it is shown or announced.

- **Splash screen and About dialog:** `ProductInformation.DisplayName` is the single source for both
  (the splash's text and window title, and the About dialog's name).
- **The rest of the UI:** the Settings page's visible *About Enigma Git Client* button and its
  automation name, and the automation names of the About buttons on the start page and the repository
  toolbar, which is what a screen reader says.
- **Linux:** the desktop entry's `Name=` and `GenericName=` (what the launcher shows), and every
  message, `--help` text and comment of `install.sh` and `uninstall.sh`.
- **Docs:** the README's Linux install section and `docs/RELEASE.md`'s install table and post-release
  checklist.

Unchanged, as planned: the technical name `Enigma.GitClient` (the window titles, the HTTP user agent,
the configuration directory) and the `enigma-git-client` install paths. An existing installation
upgrades in place. `install.sh` rewrites the desktop entry, so the launcher shows the new name after
the next install.

A new test, `DisplayNameConsistencyTests`, ties the copies no compiler links to `DisplayName`:

- the desktop entry's `Name=` and `GenericName=`;
- both scripts;
- no `.cs` or `.axaml` under `src/`, no file under `packaging/`, and not the README still saying
  "Enigma git client".

## Files / modules touched

**Modified — Core:** `Diagnostics/ProductInformation.cs` — `DisplayName`

**Modified — App:** `Views/Pages/SettingsPageView.axaml`, `Views/Pages/RepositoriesPageView.axaml`,
`Views/MainWindow.axaml`

**Modified — packaging:** `packaging/linux/enigma-git-client.desktop.in`, `packaging/linux/install.sh`,
`packaging/linux/uninstall.sh`

**Modified — docs:** `README.md`, `docs/RELEASE.md`, `docs/roadmap.md`, `docs/plan/FEATURE-3988.md`

**Tests:**

- modified: `Core.UnitTests/ProductInformationTests.cs`, `App.UnitTests/AboutDialogTests.cs` (4
  assertions), `App.UnitTests/SplashScreenTests.cs`;
- created: `Core.UnitTests/DisplayNameConsistencyTests.cs`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the drift guard lives | Core's unit tests, reading the repository's files from the solution root upward from the test output | `DisplayName` is a Core constant; the packaging files are plain text beside the solution |
| What "shipped" covers | `.cs` and `.axaml` under `src/`, everything under `packaging/` but the icons, and the README | The places the name is shown from. The earlier release notes and the roadmap history legitimately keep the old name |

## Deviations & follow-ups

- **None from the plan.**
- The window titles still read `Enigma.GitClient`, the technical name, as they did (out of scope). If
  they should read "Enigma Git Client" too, it is a one-line change in each window ViewModel.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2624 passed, 0 failed (+4). Green on the first run.
- `bash -n` passes on both scripts, and their `--help` shows the new name.
