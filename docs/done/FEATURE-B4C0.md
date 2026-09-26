# FEATURE-B4C0 — A Linux installer

**Item:** FEATURE-B4C0 — A Linux installer
**Branch:** `feature/feature-b4c0-linux-installer`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

`packaging/linux/install.sh` installs the client for the current user, the way Enigma.Tasks' installer
does, into the XDG directories and nothing else:

| What | Where |
|---|---|
| the application | `$XDG_DATA_HOME/enigma-git-client` (`~/.local/share/enigma-git-client`) |
| the launcher symlink | `$XDG_BIN_HOME/enigma-git-client` (`~/.local/bin/enigma-git-client`) |
| the desktop entry | `$XDG_DATA_HOME/applications/enigma-git-client.desktop` |
| the icon, six sizes | `$XDG_DATA_HOME/icons/hicolor/<N>x<N>/apps/enigma-git-client.png` |

The desktop entry's `Name` and `GenericName` are both **Enigma git client**; it sits under
`Development;RevisionControl;`, and its `Exec` is the absolute installed apphost.

`uninstall.sh` removes exactly those four, and never `~/.config/Enigma.GitClient` — the settings, the
repositories list, the hosting accounts and their tokens — nor any repository.

Both scripts are Enigma.Tasks' (FEATURE-393D-PHASE02) with this application's id, apphost, project and
wording: no root, no `sudo`; self-contained by default with `--framework-dependent`, `--from DIR` and
`--rid`; a staging directory swapped in, so a failed build leaves the previous install untouched; the
`DOTNET_ROOT` pin for a framework-dependent tree whose runtime is not where an apphost looks; the
desktop and icon caches refreshed best-effort. One addition specific to this application: the
installer ends with a note when git is not on the `PATH`, since the client drives the real git.

## Files / modules touched

**Created**

- `packaging/linux/install.sh`, `packaging/linux/uninstall.sh` (mode 755)
- `packaging/linux/enigma-git-client.desktop.in` — the entry, `@EXEC@` substituted at install time
- `packaging/linux/icons/enigma-git-client-{16,32,48,64,128,256}.png` — the six frames of
  `src/Enigma.GitClient.App/Assets/app.ico`, copied byte for byte (each PNG header checked against the
  size the ICO directory claims)

**Modified**

- `README.md` — an *Install on Linux* section; the *Requirements* runtime line corrected for the
  installer's self-contained default

## Deviations & follow-ups

- **Scratch-XDG side effect, not an installation artefact:** `dotnet publish` inherits `XDG_DATA_HOME`,
  so NuGet and Avalonia's build tooling put their caches (`NuGet/`, `AvaloniaUI/`) in whatever data
  directory the installer is pointed at. With the default `~/.local/share` that is where they already
  are; only a custom root shows it. Same behaviour as Enigma.Tasks' installer.
- Empty `hicolor/<N>x<N>/apps` directories are left behind by the uninstaller, as by Enigma.Tasks' — they
  belong to the icon theme, not to this application.
- Not verifiable here: running as root is refused by the first check of both scripts, and was not
  exercised.

## Build/test evidence

There is nothing to build or test in the scripts themselves; they were verified as follows, against a
scratch XDG root (`XDG_DATA_HOME`, `XDG_BIN_HOME`, `XDG_CONFIG_HOME` in the session scratchpad):

| Check | Result |
|---|---|
| `bash -n` on both scripts | clean |
| Install, self-contained (default) | 116 MB tree with `libcoreclr.so`; apphost mode 755; symlink resolves to it; 6 icons in their hicolor directories; `Exec="<absolute apphost>"` |
| `desktop-file-validate` on the written entry | no error, no warning (both installs) |
| Installed app started from `env -i` (only `HOME`, `USER`, `PATH=/usr/bin:/bin`, `DISPLAY`, `XAUTHORITY`, `XDG_RUNTIME_DIR`, a scratch `XDG_CONFIG_HOME`) | the first window opened — the git probe logged `Using git 2.55.0` |
| Reinstall over it, `--framework-dependent` | "Replacing …"; a stale file planted in the old tree is gone; no staging or `.previous` directory left; 37 MB; `Exec=env DOTNET_ROOT="/home/jo/.dotnet" "<apphost>"` because this machine has neither `/usr/share/dotnet` nor `/etc/dotnet` |
| That install started with the pinned `DOTNET_ROOT` / without it | starts / "You must install .NET to run this application." — the failure the pin exists for |
| `uninstall.sh` | the four locations removed; `settings.json` and `hidden-branches.json` in the scratch configuration byte-identical afterwards (sha256); a second run says there is nothing to remove |
| Unknown option | `uninstall: unknown option '--bogus'. Try --help.`, exit 1 |
| `sudo` in either script | none (one comment explains why root is refused) |

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors; `dotnet test --solution Enigma.GitClient.slnx`
  — 2205 passed, 0 failed (no code changed).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `Comment` | "A git client built around the commit graph and the diff" | The README's own one-line statement of what the application is about |
| `Keywords` | git, version control, repository, commit, branch, merge, diff, history | What someone typing into a launcher would look for |
| The git note | A closing line when `git` is not on `PATH` | The one prerequisite the runtime bundling cannot cover; cheaper to fix before the first start |
| The final hint | `enigma-git-client`, or `enigma-git-client <repository>` | The application opens a repository given on its command line |
