# FEATURE-42B2 — Release 5.1.1

**Item:** FEATURE-42B2 — Release 5.1.1
**Branch:** `feature/feature-42b2-release-5-1-1`
**Run:** feature/2026-10-02-release-5-1-1

## Summary

Cuts **5.1.1**, a PATCH release under Semantic Versioning that ships BUG-7E41, the only work merged
since the `5.1.0` tag. The application's program is now `Enigma.GitClient.Desktop` (it was
`Enigma.GitClient.App`), like every other Enigma desktop application. Its test project is
`Enigma.GitClient.Desktop.UnitTests`. The `enigma-git-client` command, the launcher entry, the install
directory and everything stored are unchanged.

**Why 5.1.1:** this is what was asked, and SemVer agrees. The rename adds and removes no behaviour and
changes nothing stored. The one thing a user can trip on, starting the old program by its file name,
is covered by the upgrade note.

- `Directory.Build.props`: `<Version>5.1.1</Version>`.
- `RELEASENOTES.md`: a dated `## 5.1.1 — 2026-10-02` section on top, in the file's shape: a summary,
  then:
  - *Fixes*: the application's files take the Enigma desktop name. On Linux, the window class and
    the desktop entry follow it, and `uninstall.sh` recognises a 5.1.0 installation;
  - *Upgrading from 5.1.0*:
    - run `install.sh` again;
    - a direct start of `Enigma.GitClient.App` becomes `Enigma.GitClient.Desktop`;
    - publish into an empty folder;
    - nothing is migrated, and the stored files are named;
    - building from source;
  - *Dependencies* (nothing bumped; the Avalonia set held);
  - *Version*.
- `README.md`: the callout is *What's new in 5.1.1*: the rename and the reinstall, then what 5.1
  brought, and the pointer for a 4.x reader is kept.
- `SECURITY.md`: unchanged. 5.1.x is already the supported line, and 5.1.1 is inside it.
- `docs/RELEASE.md`: the MSI paragraph lists 5.1.1 among the releases without a profile, rewrapped to
  the file's width.

## Runbook — printed, not run

```bash
# 1. Pre-flight — already run in this dev, in Release (see below)
dotnet build Enigma.GitClient.slnx -c Release
dotnet test --solution Enigma.GitClient.slnx -c Release

# 2. Integrate: the run branch into develop, then develop into main
git switch develop
git merge --no-ff feature/2026-10-02-release-5-1-1
git switch main
git merge --no-ff develop

# 3. Tag — bare X.Y.Z, as 1.0.0 to 5.1.0 are
git tag 5.1.1

# 4. Push
git push origin develop main
git push origin 5.1.1

# 5. Publish (into an empty folder) and install (Linux)
rm -rf ./artifacts
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
packaging/linux/install.sh
```

Then *Post-release verification* from `docs/RELEASE.md`:

- the splash screen and About say `Version 5.1.1`;
- **Enigma Git Client** starts from the launcher, and its running window is grouped under the
  launcher's icon (the window class is `Enigma.GitClient.Desktop` now);
- `readlink ~/.local/bin/enigma-git-client` ends in `/Enigma.GitClient.Desktop`.

## Files / modules touched

**Modified**

- `Directory.Build.props`
- `RELEASENOTES.md`
- `README.md`
- `docs/RELEASE.md`
- `docs/roadmap.md`: the row's status
- `docs/plan/FEATURE-42B2.md`: status

**Created**

- `docs/done/FEATURE-42B2.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the notes name the program | "the program", with its file name; `.exe` on Windows | Users meet it as a file in the install or publish folder. "Assembly" is a builder's word |
| Window grouping in the notes | One bullet under *Fixes* | It is what a Linux user sees: the launcher still groups the window, because the desktop entry follows the new name |
| The stale-publish warning | In *Upgrading from 5.1.0* | `dotnet publish -o` does not empty its folder. A publish over a 5.1.0 one keeps `Enigma.GitClient.App*` beside the new files, which is confusing on Windows, where there is no installer |
| Contributors | One *Building from source* bullet | The project and test paths moved. The README already shows the new `dotnet run` line |
| The runbook's publish step | `rm -rf ./artifacts` before the publish | The same stale-files point, applied to the release itself |

## Deviations & follow-ups

- None from the plan.
- **Stale build output:** `src/Enigma.GitClient.Desktop/bin/Release/net10.0/` still holds the 5.1.0
  build's `Enigma.GitClient.App*` files. They are ignored and the build does not use them.
  `dotnet clean -c Release`, or deleting `bin/` and `obj/`, tidies them before a publish or an
  install `--from` that directory.
- The Avalonia set (12.1.1 → 12.1.3) is still held back with Enigma.Avalonia.Desktop 1.2.0.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

This dev is the documentation update: the notes, the README and `docs/RELEASE.md`. No `CLAUDE.md` or
`AGENTS.md` exists. `uninstall.sh`'s comment ("the launcher's name up to 5.1.0") stays right with
5.1.1 released. No other prose doc names the current version.

## Build/test evidence

- `git tag --list 5.1.1` was empty.
- `dotnet list package --outdated`: only the Avalonia set (Avalonia, Avalonia.Desktop,
  Avalonia.Fonts.Inter, Avalonia.Themes.Fluent; Avalonia.Headless and Avalonia.Skia in the tests),
  12.1.1 → 12.1.3. Held back. No other update.
- `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors. This is
  the first Release build since the rename (BUG-7E41 was verified in Debug).
- `dotnet test --solution Enigma.GitClient.slnx -c Release`: **2647 passed**, 0 failed, 0 skipped.
- The Release `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
  `5.1.1+2c2083caf821f07875125dc51b4460c73990690d`, the run-branch commit this dev was cut from. The
  Release output's launcher is `Enigma.GitClient.Desktop`.
- Nothing in this dev ran `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.
- Fix budget: 0 cycles used.
