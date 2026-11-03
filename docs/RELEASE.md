# Release runbook

Reusable checklist for cutting a new **Enigma.GitClient** version.

Replace `X.Y.Z` with the version being released (e.g. `1.1.0`) throughout. The version lives in
`Directory.Build.props` (`<Version>`) and covers the whole solution: `Enigma.GitClient.Core` and
`Enigma.GitClient.Desktop` ship as one application, so they never carry separate numbers.

**Nothing here is published to NuGet.** Neither project has a `PackageId`, so there is no `pack`
step, no `nuget push`, and no package metadata to keep in order. What a release produces is a tag on
`main` and, for whoever wants it, an installed application. Steps 5 and 6 replace what would be the
pack-and-push steps for a library.

## 1. Pre-release checks

Run from the repository root, on the branch that will be merged:

- [ ] `<Version>X.Y.Z</Version>` set in `Directory.Build.props`.
- [ ] `RELEASENOTES.md` has a top `X.Y.Z` section, dated, describing the release (newest first; an
      `unreleased` heading renamed to the date).
- [ ] The README's *what's new* callout reflects `X.Y.Z`.
- [ ] Clean, warning-free build:
      ```bash
      dotnet build Enigma.GitClient.slnx -c Release
      ```
- [ ] Full test suite green (it needs `git` on the `PATH`: the integration tests drive a real one):
      ```bash
      dotnet test --solution Enigma.GitClient.slnx -c Release
      ```
- [ ] The running application says `Version X.Y.Z` in its **About** dialog.
      To read it off the build instead (`strings` is in binutils) — it prints the version and the
      commit the build was cut from, `X.Y.Z+<sha>`, which is what the About dialog's build line
      shortens:
      ```bash
      strings src/Enigma.GitClient.Desktop/bin/Release/net10.0/Enigma.GitClient.Core.dll | grep -m1 -o 'X\.Y\.Z+[0-9a-f]*'
      ```

## 2. Merge into `develop`, then into `main`

Work is integrated on `develop`; `main` is the default branch and carries the releases:

```bash
git switch develop
git merge --no-ff <release-branch>
git switch main
git merge --no-ff develop
```

## 3. Tag the release

The first release sets the convention: **bare `X.Y.Z`**, no `v` prefix. Run `git tag` before tagging
a later release and match what you find.

```bash
git tag X.Y.Z
```

## 4. Push

```bash
git push origin develop main
git push origin X.Y.Z
```

## 5. Publish the application

A release build for a specific platform, with the .NET runtime bundled so the result runs on a machine
that has no .NET installed:

```bash
dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained true -o ./artifacts
```

Substitute the runtime identifier for the target platform — `linux-arm64`, `win-x64`.

**Always pass `-r`.** A publish with no runtime identifier carries the Skia and HarfBuzz natives for
*every* platform in a `runtimes/` tree; with one, the natives sit beside the assemblies and the result
is about 116 MB self-contained, or 37 MB against an installed runtime (`--self-contained false`).

The publish scripts run that publish from any directory, compiled ReadyToRun, against an installed
.NET 10 runtime: `packaging/windows/publish.ps1` and `publish.bat` for `win-x64`,
`packaging/linux/publish.sh` for `linux-x64`.

```powershell
packaging\windows\publish.ps1
# Under Windows PowerShell's default execution policy, which refuses local scripts:
powershell -ExecutionPolicy Bypass -File packaging\windows\publish.ps1
# Or, from a cmd prompt, with no execution policy involved:
packaging\windows\publish.bat
```

```bash
packaging/linux/publish.sh
```

The result is in `src/Enigma.GitClient.Desktop/bin/Release/net10.0/<rid>/publish/`, with the
application icon beside the executable as `app.ico`.

## 6. Install it

On Linux, the installer does the publish and the installation in one step, and is what puts the
application in the desktop launcher:

```bash
packaging/linux/install.sh              # --help lists the options
```

It writes only under `$HOME`:

| What | Where |
|---|---|
| the application | `~/.local/share/enigma-git-client` |
| the launcher symlink | `~/.local/bin/enigma-git-client` |
| the desktop entry, "Enigma Git Client" | `~/.local/share/applications/enigma-git-client.desktop` |
| the icon, eight sizes | `~/.local/share/icons/hicolor/<N>x<N>/apps/enigma-git-client.png` |

`packaging/linux/uninstall.sh` removes exactly those and leaves `~/.config/Enigma.GitClient` — the
settings, the repositories list, the accounts and their tokens — alone.

On Windows, build the MSI and install it:

```powershell
packaging\windows\buildmsi.ps1          # or buildmsi.bat from a cmd prompt
```

It runs `git clean -dxf` first, which deletes every file git does not track. Commit or move anything
new before running it. Then it publishes (step 5) and packages the result with Enigma.Msi into
`artifacts\Enigma.GitClient.X.Y.Z.msi`, beside the `.msipkg.json` profile it was built from.

| What | Where |
|---|---|
| the application, for all users | `%ProgramFiles%\Enigma Git Client` (32-bit package: `Program Files (x86)` on 64-bit Windows) |
| the shortcuts, "Enigma Git Client" | the desktop and the program menu |
| the entry in *Installed apps* | "Enigma Git Client", publisher "Josué Clément" |

Installing it replaces an earlier version installed from an MSI, and uninstalling it from *Installed
apps* leaves `%APPDATA%\Enigma.GitClient` (the settings, the repositories list, the accounts and their
tokens) alone.

## 7. Post-release verification

- [ ] **Enigma Git Client** appears in the application launcher, with its icon, and starts when
      clicked. A launcher starts a process from a bare environment, so this is not the same test as
      starting it from a terminal — check the launcher, not just the shell.
- [ ] On Windows, after installing the MSI, *Installed apps* lists one **Enigma Git Client**, at
      `X.Y.Z`, and the desktop and program-menu shortcuts start it.
- [ ] The **About** dialog says `Version X.Y.Z`, and its build line names the tagged commit.
- [ ] A repository opens, its history draws, and a branch hidden in the branches dialog stays hidden
      after a restart.
- [ ] `git tag` lists `X.Y.Z` on the merge commit on `main`, and the tag is on the remote.

## The Windows installer's identity

There is no committed MSI profile per version. `buildmsi.ps1` writes the profile on every build from
`Directory.Build.props` (the version) and its own constants, so a release changes nothing for the
installer.

- **`upgradeCode`** is `871622d1-a9c6-4c72-b6a3-e8d8104ad5c3`, written once in `buildmsi.ps1`. It is
  the code the MSI that installed 6.13.0 carries. It must never change: an MSI with another code
  installs beside the old version instead of replacing it. `WindowsInstallerIdentityTests` pins it.
- **`productId`** is new for every build.

The MSI that installed 6.13.0 was built outside this repository with Enigma.Msi. The script keeps its
upgrade code. The `dotnet-release` skill's committed `msiProfiles/` was declined for every release from
1.0.0 to 6.13.0, and is not needed now.
