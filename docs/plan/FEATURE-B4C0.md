# FEATURE-B4C0 — A Linux installer

**Status:** DONE — see `docs/done/FEATURE-B4C0.md`
**Type:** FEATURE
**Branch:** `feature/feature-b4c0-linux-installer`
**Run:** feature/2026-09-26-release-1-0-0

## Objective

Make the client installable on Linux with one command, the way Enigma.Tasks is: the application in
`~/.local/share`, a launcher symlink in `~/.local/bin`, a desktop entry named **Enigma git client**
(both `Name` and `GenericName`) with its icon in the hicolor theme — and an uninstaller that takes it
all away again.

## Context & constraints

- **The reference** is Enigma.Tasks' `packaging/linux/` (FEATURE-393D-PHASE02): `install.sh` (bash,
  `set -euo pipefail`, refuses root, `--from DIR`, `--framework-dependent`, `--rid RID`, `--help`;
  publishes the app in Release — self-contained by default — into a staging directory beside the
  install directory and swaps it in; symlinks the apphost; installs six icons into hicolor; writes the
  desktop entry from `enigma-tasks.desktop.in` by substituting `@EXEC@` in bash, not sed; pins
  `DOTNET_ROOT` in `Exec` when a framework-dependent tree's runtime is not where an apphost looks;
  refreshes the desktop and icon caches best-effort; warns when the bin directory is off `PATH`),
  `uninstall.sh` (removes exactly those four locations, never the user's configuration, refuses to
  remove a directory whose name is not the app id, leaves a symlink alone that points elsewhere), and
  `icons/enigma-tasks-<N>.png`.
- **Why self-contained by default**: Enigma.Tasks' dry run showed that a framework-dependent apphost
  never finds a runtime through `PATH`, so a launcher started from a bare environment does nothing
  when the runtime lives in `~/.dotnet` — which is where it lives on this machine.
- The app is `src/Enigma.GitClient.App`, apphost `Enigma.GitClient.App`, configuration in
  `$XDG_CONFIG_HOME/Enigma.GitClient`. `Assets/app.ico` holds six PNG frames at exactly the hicolor
  sizes (16, 32, 48, 64, 128, 256). It accepts a repository path as its first argument.
- `.gitattributes` already declares `*.sh text eol=lf` and `*.png binary`.
- The README says "the published app is framework-dependent and needs the matching runtime".
- `desktop-file-validate`, `update-desktop-database`, `gtk-update-icon-cache` and `kbuildsycoca6` are
  on this machine.

## Steps

1. `packaging/linux/icons/enigma-git-client-<N>.png` — the six frames of `app.ico`, byte copies.
2. `packaging/linux/enigma-git-client.desktop.in` — `Type=Application`, `Version=1.5`,
   `Name=Enigma git client`, `GenericName=Enigma git client`, a `Comment`, `Exec=@EXEC@`,
   `Icon=enigma-git-client`, `Terminal=false`, `Categories=Development;RevisionControl;`, `Keywords=`,
   `StartupNotify=true`, `StartupWMClass=Enigma.GitClient.App`.
3. `packaging/linux/install.sh` — Enigma.Tasks' installer with this application's id, apphost,
   project and wording.
4. `packaging/linux/uninstall.sh` — likewise; it names `~/.config/Enigma.GitClient` as left untouched.
5. Both scripts mode 755 in the index.
6. `README.md` — an *Install on Linux* section (what goes where, the options, the uninstaller), and the
   *Requirements* line about the runtime corrected for the installer's default.

## Acceptance criteria

- `bash -n` parses both scripts.
- `desktop-file-validate` accepts the written entry with no error and no warning, and its `Name` and
  `GenericName` are both "Enigma git client".
- A dry run against a scratch XDG root installs the application: the install directory holds a
  runnable apphost, the symlink resolves to it, the six icons are in their hicolor directories, and
  `Exec` is the absolute installed path; the installed app starts from a bare environment.
- Running the installer again replaces the installation and leaves no stale file.
- `uninstall.sh` removes the four locations and leaves the scratch configuration directory untouched.
- Neither script uses `sudo` or writes outside the XDG directories.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- `.deb`, `.rpm`, Flatpak, AppImage, a system-wide install.
- Windows and macOS packaging.
- Registering the application for folders (`MimeType=inode/directory`).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| "The same way as Enigma.Tasks" | Its final `install.sh` / `uninstall.sh` / template / icon layout, adapted | The draft asks for exactly that, and those scripts carry lessons (the runtime lookup) already paid for | A new design |
| Publish default | Self-contained, with `--framework-dependent` as the opt-in | Enigma.Tasks' verified finding: a framework-dependent launcher entry silently does nothing when the runtime is in `~/.dotnet` | Framework-dependent by default |
| The application id | `enigma-git-client` (directory, symlink, desktop file, icon name) | Mirrors `enigma-tasks` and the human name the draft gives | `enigma-gitclient`; a reverse-DNS id (none is configured anywhere) |
| `Name` / `GenericName` | Both "Enigma git client" | Required by the draft | A generic `GenericName` such as "Git client" |
| `Categories` | `Development;RevisionControl;` | The freedesktop main category and the registered additional one for a version-control tool | `Utility;` |
| `Exec` | The absolute installed apphost, no field code | Does not depend on `~/.local/bin` being on the launcher's `PATH`; without a `MimeType` a `%f` would never be filled | `Exec=enigma-git-client`; `%f` |
| How it is verified | `bash -n`, `desktop-file-validate`, and a real install / reinstall / uninstall into a scratch XDG root, then a start from `env -i` | The Definition of Done for scripts, without a shell-test framework the project does not have | A `bats` suite; inspection alone |
