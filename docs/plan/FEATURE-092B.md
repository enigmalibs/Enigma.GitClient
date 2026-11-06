# FEATURE-092B — Release 6.18.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-092b-release-6-18-0`
**Run:** feature/2026-11-06-release-6-18-0

## Objective

Cut **6.18.0**, using FEATURE-9D08 (6.17.0) as the template and following the `dotnet-release` skill.
The release covers:

- the version;
- the release notes for what 6.18.0 adds beyond 6.17.0;
- the README callout, and the README lines the release's features left for it;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.17.0` today).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.18.0 (z is 0).
- **`buildmsi.ps1` has no version of its own**: it reads `/Project/PropertyGroup/Version` from
  `Directory.Build.props`.
- **6.18.0 carries** what was built since the `6.17.0` tag (`git log 6.17.0..HEAD`), all `DONE`:
  - FEATURE-4F5C (PHASE01–04): the start window's repository tiles draw their path in the secondary
    grey and a 24 px glyph; a tile has a context menu (open, open in a new window, the folder, a
    terminal, copy the path, forget, choose an icon, use the default icon); a repository can have an
    icon of its own, copied into `<config>/repository-icons/`, shared by every profile;
  - FEATURE-0CDE: the History search moved to a bar of its own under the command bar, shown by a
    *Search* toggle or Ctrl+F; the box is twice as wide; Escape backs out one layer at a time; hiding
    the bar clears the search;
  - BUG-553F: a disabled menu item's glyph greys like its title.
- **README debts the features left to this release:**
  - `docs/done/FEATURE-4F5C-PHASE04.md` says the README and release notes do not mention the tile's
    menu or icons yet, and that the next release item describes them;
  - *Where your things are kept* lists the config directory's files and says "Nothing else is written
    anywhere"; `repository-icons/` (FEATURE-4F5C-PHASE03) is now written there too.
- **No Core source changed** since `6.17.0` (`git diff --stat 6.17.0..HEAD -- src/Enigma.GitClient.Core`
  is empty): no public Core API moves. `repository-lists.json` is unchanged: `ListedRepository.IconFile`
  is `[JsonIgnore]`, and `RepositoryListStore.CurrentVersion` stays 1.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against (6.17.0: held at 12.1.4 with Enigma.Avalonia.Desktop 1.3.0).
- **This machine is Linux.** The run that built 4F5C/0CDE/553F had a clean Linux baseline (0 warnings,
  0 failures). The release is measured against this run's baseline, taken in Release on the run branch.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1` (it starts
  with `git clean -dxf`, and needs Windows).

## Steps

1. Check that `git tag --list 6.18.0` is empty.
2. `Directory.Build.props`: `<Version>6.18.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.18.0 — <date>` section on top, in 6.17.0's shape:
   - a summary list;
   - one section per change: the repository tiles (menu, icon, look), the History search bar, the
     disabled menu glyphs;
   - *Upgrading from 6.17*, *Dependencies*, *Version*.

   Every path, file, menu text, limit and number it quotes (MSI name, stored-format versions, the
   icon folder, the size limits, the accepted kinds of file) is checked against the files.
4. `README.md`:
   - the callout names 6.18 and sums up 6.17 after it; 6.16's sentence goes, as 6.15's did in 6.17.0.
     The older upgrade notes stay;
   - the start-window feature bullet gains the tile's menu and the repository's own icon;
   - *Where your things are kept* gains `repository-icons/`, and its closing sentence stays true.
5. `SECURITY.md`: 6.18.x supported; 6.17.x no longer.
6. `docs/RELEASE.md`: any line naming the current version is brought to 6.18.0; history stays.
7. Dependency check (`dotnet list Enigma.GitClient.slnx package --outdated`), logged. Non-coupled patch
   and minor bumps are applied; the Avalonia set is held unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build (`--no-incremental`) with zero warnings, the Debug build too;
   - the whole suite against the baseline, no new failure;
   - the classes that read the version (`ProductInformationTests`, `HostHttpTests`,
     `DisplayNameConsistencyTests`, `AboutDialogTests`) run on their own and pass;
   - the built Core and Desktop assemblies carry `6.18.0`.

## Acceptance criteria

- The Release build is clean with zero warnings; the whole suite shows no failure the baseline lacks.
- The built assemblies carry 6.18.0, and the version-reading test classes pass.
- `RELEASENOTES.md` covers FEATURE-4F5C, FEATURE-0CDE and BUG-553F, and its quoted paths, texts and
  numbers match the files.
- The README callout names 6.18; the README describes the tile's menu and icon, and lists
  `repository-icons/` among the things kept. `SECURITY.md` supports 6.18.x only.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack`, `buildmsi`, or installs
  an MSI.

## Out of scope

- NuGet packaging; building the MSI; moving the Avalonia set on its own.
- `HistoryPageView.MoveFocus`'s focus-on-nothing after Escape leaves the diffs: a pre-existing
  follow-up recorded in `docs/done/FEATURE-0CDE.md`.
- Removing a forgotten repository's icon: planned out of scope by FEATURE-4F5C.
- Merging into `develop`/`main` and tagging: the user's, after the run.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.18.0 | The user's rule, x.y+1.z with z = 0; new behaviour, no Core API change: a minor release | 6.17.1 (tile menus and icons are features, not fixes); 7.0.0 (nothing breaks) |
| What it carries | FEATURE-4F5C, FEATURE-0CDE and BUG-553F | Exactly the commits since the `6.17.0` tag | Waiting for more items |
| README callout | 6.18, then 6.17; 6.16's sentence goes | House shape: the newest two releases, then the older upgrade notes | Keeping 6.16's sentence |
| README body | The start-window bullet gains the tile's menu and icon; *Where your things are kept* gains `repository-icons/` | FEATURE-4F5C-PHASE04 left the description to this release; the storage table and "Nothing else is written anywhere" are no longer true without it | Callout only, as in 6.17.0 (would leave the storage section wrong) |
| The History search bullet | Unchanged | It stays true: the search still highlights and steps; where its box sits is the notes' job | Rewriting it for the bar (gold-plating a release) |
| Shape of notes, callout, tag | As 6.17.0 | House convention | A single *Changes* list |
| Installer | No edit to `buildmsi`; the `<Version>` bump is what it builds | It reads `Directory.Build.props` | Hard-coding the version in the script |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held unless Enigma.Avalonia.Desktop moved | House rule from earlier releases | Bumping everything |
| Pre-flight configuration | Release, baseline in Release too; the Debug build checked for warnings | `docs/RELEASE.md` pre-flights in Release; a baseline is only comparable in the same configuration | Debug only |
| Phasing | One phase | A routine release (`dotnet-release`) | Splitting docs from the version |
