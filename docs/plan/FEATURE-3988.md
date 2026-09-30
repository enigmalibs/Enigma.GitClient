# FEATURE-3988 — The name Enigma Git Client

**Status:** DONE — see `docs/done/FEATURE-3988.md`
**Type:** FEATURE
**Branch:** `feature/feature-3988-enigma-git-client-name`
**Run:** feature/2026-09-30-rename-and-release

## Objective

Rename the application, wherever its name is shown, from "Enigma git client" to **"Enigma Git
Client"**: the splash screen, the About dialog, and the Linux installer and uninstaller.

## Context & constraints

- `Core/Diagnostics/ProductInformation.DisplayName` ("Enigma git client") is the single source for:
  - the splash screen: its text and its window title (`SplashWindow.axaml`, `x:Static`);
  - the About dialog (`AboutViewModel.DisplayName`).
- The same words are also written literally in:
  - `SettingsPageView.axaml`: the visible **"About Enigma git client"** button, and its automation
    name;
  - the automation name of the About button in `RepositoriesPageView.axaml` and `MainWindow.axaml`;
  - `packaging/linux/enigma-git-client.desktop.in`: `Name=` and `GenericName=`, which is what the
    launcher shows;
  - `packaging/linux/install.sh` (header, `--help`, progress and final messages) and
    `packaging/linux/uninstall.sh` (`--help` and messages);
  - `README.md` (the Linux install section) and `docs/RELEASE.md` (the install table and the
    post-release checklist).
- `ProductInformation.Name` ("Enigma.GitClient") is a different, technical name. It is used for the
  window titles, the HTTP user agent (no spaces allowed), the configuration directory and the
  `enigma-git-client` install paths. It is not the display name and does not change.
- Tests asserting the old name: `ProductInformationTests`, `AboutDialogTests` (4), `SplashScreenTests`.
- Earlier sections of `RELEASENOTES.md` name the product as it was called then.

## Steps

1. `ProductInformation.DisplayName` = `"Enigma Git Client"`.
2. The literal copies: the Settings page's button text and automation name, the two other About
   automation names, the desktop entry's `Name=` and `GenericName=`, and every message and comment
   of `install.sh` and `uninstall.sh`.
3. `README.md` and `docs/RELEASE.md`: the current name.
4. Tests:
   - the existing assertions move to the new name;
   - a new drift guard: the desktop entry's `Name=` is `ProductInformation.DisplayName`, both scripts
     say it, and neither the scripts, the entry nor the views carry the old spelling.

## Acceptance criteria

- The splash screen, the About dialog, the Settings page's About button, the launcher entry and the
  installer and uninstaller all say "Enigma Git Client".
- Nothing under `src/`, `packaging/`, `README.md` or `docs/RELEASE.md` still says "Enigma git client".
- The technical name, the window titles, the configuration directory and the install paths are
  unchanged.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The window titles and `ProductInformation.Name` (`Enigma.GitClient`).
- The install directory, the executable and desktop-file names (`enigma-git-client`), and the
  configuration directory.
- Earlier release notes.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How far the rename reaches | Every place the display name is shown or announced, the Settings page's button and the automation names included | The Settings button shows the name as plainly as the About dialog. A screen reader announcing the old spelling is the same inconsistency, heard instead of seen | Only the splash, About and the scripts (leaves a visible "About Enigma git client") |
| The technical name | Unchanged | It is not the display name, and changing it would move the configuration directory and the install paths under every existing user | Renaming everything |
| Earlier release notes | Unchanged | They record what shipped. The 5.0.0 notes say the name changed | Rewriting history |
| A drift guard | A test tying the desktop entry and the scripts to `DisplayName` | The name lives in four files that no compiler links. This rename is exactly the change they drift on | No test |
| `GenericName=` | Also "Enigma Git Client" | It mirrored `Name=` and stays in step. Making it generic ("Git client") is a separate choice | Changing its meaning in a rename |
