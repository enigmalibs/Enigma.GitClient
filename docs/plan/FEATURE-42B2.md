# FEATURE-42B2 — Release 5.1.1

**Status:** DONE — see `docs/done/FEATURE-42B2.md`
**Type:** FEATURE
**Branch:** `feature/feature-42b2-release-5-1-1`
**Run:** feature/2026-10-02-release-5-1-1

## Objective

Cut **5.1.1**, a PATCH release that ships BUG-7E41: the application is now `Enigma.GitClient.Desktop`,
like every other Enigma desktop application. FEATURE-28C8 (4.1.1, the last patch) and FEATURE-0C53
(5.1.0) are the template:

- the version;
- the release notes, naming the rename and what it means for an upgrade;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`;
- a dependency check;
- a Release pre-flight;
- the merge/tag/push runbook, printed for the user.

## Context & constraints

- **An app, not a package** (no `PackageId`). `docs/RELEASE.md` is the runbook. A release is a bare
  `X.Y.Z` tag on `main`, and the version lives in `Directory.Build.props` (`5.1.0` today).
- **Tags:** `1.0.0` … `5.1.0`; `5.1.0` is on `main` (`9841842`). `RELEASENOTES.md`'s top section is
  5.1.0.
- **In this release:** BUG-7E41 alone, the only work merged into `develop` since the `5.1.0` tag.
  - The directory, project file, assembly, root namespace, XAML classes, `avares://` URIs and Windows
    manifest are `Enigma.GitClient.Desktop`. The launcher the build produces is
    `Enigma.GitClient.Desktop` (`.exe` on Windows), and the X11 window class follows it.
  - The test project is `Enigma.GitClient.Desktop.UnitTests`.
  - Linux packaging: `install.sh` publishes `src/Enigma.GitClient.Desktop`; the desktop entry's
    `StartupWMClass` is `Enigma.GitClient.Desktop`; `uninstall.sh` also removes a symlink pointing at
    the former `Enigma.GitClient.App` launcher. The `enigma-git-client` command, the install
    directory, the application id and the icons are unchanged.
  - Nothing stored moves: the configuration folder (`AppPaths.FolderName`) and the product name
    (`ProductInformation.Name`) are constants.
- **What an upgrade must know** (from BUG-7E41's completion records):
  - On Linux, `install.sh` replaces the install directory and re-points the symlink, and writes the
    desktop entry with the new window class.
  - Anyone who starts `Enigma.GitClient.App` directly (a script, a shortcut) must start
    `Enigma.GitClient.Desktop` instead.
  - `dotnet publish -o <dir>` does not empty `<dir>`: a publish over a 5.1.0 one leaves the old
    `Enigma.GitClient.App` files beside the new ones.
- **Semantic Versioning:** nothing added, removed or changed in behaviour, nothing stored changes: a
  **PATCH**, 5.1.1, which is also what was asked.
- `SECURITY.md` supports the latest minor line, 5.1.x; 5.1.1 is inside it.
- The Avalonia set (12.1.1) moves only as a whole, with what Enigma.Avalonia.Desktop is built against.
- A run never tags, pushes or merges into `develop`/`main`. Those commands are printed.

## Steps

1. Check `git tag --list 5.1.1` is empty.
2. `Directory.Build.props`: `<Version>5.1.1</Version>`.
3. `RELEASENOTES.md`: a dated `## 5.1.1 — 2026-10-02` section on top, in the file's shape:
   - a summary;
   - *Fixes*: the application's files carry the Enigma desktop name;
   - *Upgrading from 5.1.0*: reinstall, a direct start by the old name, a publish into an empty
     folder, nothing migrated, building from source;
   - *Dependencies* (nothing bumped; the Avalonia set held);
   - *Version*.
4. `README.md`: the what's-new callout names 5.1.1 and its change, then what 5.1 brought, and keeps
   the pointer for a 4.x reader.
5. `SECURITY.md`: check 5.1.x is the supported line.
6. `docs/RELEASE.md`: the MSI paragraph records 5.1.1 among the releases without a profile.
7. Dependency check (`dotnet list package --outdated`), logged. Nothing bumped in a PATCH.
8. Pre-flight in Release: build clean, suite green, the assemblies carry `5.1.1`, and the Release
   output is the `Enigma.GitClient.Desktop` launcher.
9. Print the runbook.

## Acceptance criteria

- Release build clean with zero warnings; Release suite green.
- The built assemblies carry 5.1.1, and the launcher is `Enigma.GitClient.Desktop`.
- `RELEASENOTES.md` describes the rename and the upgrade from 5.1.0. The README callout names 5.1.1,
  and `SECURITY.md` covers it.
- Nothing in the dev runs `git tag`, `git push`, `dotnet pack` or `dotnet nuget push`.

## Out of scope

- NuGet packaging; a Windows MSI profile.
- Any dependency bump, the Avalonia set included.
- Rewriting released `RELEASENOTES.md` sections that name `Enigma.GitClient.App` (e.g. 4.1.1's): they
  describe what was true then.
- Tagging, merging, pushing.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 5.1.1 | As asked. A rename with no behaviour added or removed and nothing stored changing is a PATCH. The command users run, `enigma-git-client`, is unchanged | 5.2.0 (nothing new); 6.0.0 (the launcher's file name is not a public interface, and the upgrade note covers anyone who used it) |
| What the release contains | BUG-7E41 alone | It is the only work since the `5.1.0` tag | Waiting for more work |
| Where the rename goes in the notes | *Fixes* | It was filed as a bug (BUG-7E41), the precedent FEATURE-0C53 recorded | A section of its own; *Breaking Changes* |
| The upgrade note | *Upgrading from 5.1.0*: reinstall, a direct start by the old name, publish into an empty folder, nothing migrated, building from source | Those are the only places a user or a contributor can trip | "Nothing changes", which would be wrong for a direct start |
| Dependency refresh | Checked and logged; nothing bumped | A patch ships only its change (FEATURE-28C8's rule). The Avalonia set stays with Enigma.Avalonia.Desktop 1.2.0 | Applying non-coupled bumps (the minor-release rule) |
| The README callout | "What's new in 5.1.1" naming the rename, then what 5.1 brought, with the 4.x pointer kept | FEATURE-28C8's shape: a reader sees both the patch and the minor's features | Replacing 5.1's features; leaving the callout at 5.1 |
| `SECURITY.md` | Unchanged: 5.1.x already supported | 5.1.1 is inside the latest minor line | Adding a 5.1.1 row |
| MSI profile, tag | None; bare `5.1.1`, printed | The conventions 1.0.0 to 5.1.0 set | Generating an MSI; `v5.1.1` |
| Breakdown | One dev | A release is one reviewable commit of version and documents | Splitting notes from the version |
