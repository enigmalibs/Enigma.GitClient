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
- [ ] The running application says `Version X.Y.Z` on its splash screen and in its **About** dialog.
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
| the icon, six sizes | `~/.local/share/icons/hicolor/<N>x<N>/apps/enigma-git-client.png` |

`packaging/linux/uninstall.sh` removes exactly those and leaves `~/.config/Enigma.GitClient` — the
settings, the repositories list, the accounts and their tokens — alone.

Windows has no installer: publish as in step 5 and run the result.

## 7. Post-release verification

- [ ] **Enigma Git Client** appears in the application launcher, with its icon, and starts when
      clicked. A launcher starts a process from a bare environment, so this is not the same test as
      starting it from a terminal — check the launcher, not just the shell.
- [ ] The splash screen and the **About** dialog say `Version X.Y.Z`, and the About dialog's build line
      names the tagged commit.
- [ ] A repository opens, its history draws, and a branch hidden in the branches dialog stays hidden
      after a restart.
- [ ] `git tag` lists `X.Y.Z` on the merge commit on `main`, and the tag is on the remote.

## Why there is no MSI profile

`dotnet-release` offers a WixSharp MSI profile for an app release. It was declined for 1.0.0, 1.1.0,
2.0.0, 3.0.0, 3.1.0, 4.0.0, 4.1.0, 4.1.1, 5.0.0 and 5.1.0: the releases' packaging is the Linux
installer. When a Windows installer is wanted, generate the **first** profile then — its
`upgradeCode` is created once and reused verbatim in every later version, while `productId` is new
each time.
