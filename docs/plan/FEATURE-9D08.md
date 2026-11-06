# FEATURE-9D08 — Release 6.17.0

**Status:** DONE
**Type:** FEATURE
**Branch:** `feature/feature-9d08-release-6-17-0`
**Run:** feature/2026-11-06-release-6-17-0

## Objective

Cut **6.17.0**, using FEATURE-2D0A (6.16.0) as the template and following the `dotnet-release` skill.
The release covers:

- the version;
- the release notes for what 6.17.0 adds beyond 6.16.0;
- the README callout;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.16.0` today).
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.17.0 (z is 0).
- **`buildmsi.ps1` has no version of its own**: it reads `/Project/PropertyGroup/Version` from
  `Directory.Build.props`, matched against `^\d+\.\d+\.\d+$`.
- **6.17.0 carries** what was built since the `6.16.0` tag (`git log 6.16.0..HEAD`), both `DONE`:
  - FEATURE-0A3D: every dialog opens with the focus on its first text field, else its default button;
    a red question focuses Cancel; and every question that removes something or throws work away is
    now red (PHASE01 and PHASE02, `docs/done/FEATURE-0A3D-PHASE0*.md`);
  - FEATURE-83E5: a branch or tag badge's menu holds the badge's own items only again. The line's items
    that 6.16.0 appended there stay on the line's menu. Its done doc leaves it to this release to say
    so; the 6.16.0 notes stay as written (history).
- **No Core source changed** since `6.16.0` (`git diff --stat 6.16.0..HEAD -- src/Enigma.GitClient.Core`
  is empty): no public Core API moves.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only together with what Enigma.Avalonia.Desktop is
  built against. At planning (2026-11-06) the set is 12.1.4, 12.1.5 is out, and Enigma.Avalonia.Desktop
  1.3.0 is still the latest.
- **Tests on this machine (Windows):** the Desktop test host crashes part-way, and about 35 tests fail
  for environmental reasons (read-only git objects at cleanup, DPAPI, CRLF), all pre-existing — see
  `docs/done/FEATURE-0A3D-PHASE01.md`. The release is measured against the run's baseline, taken in
  the Release configuration on the run branch.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1` (it starts
  with `git clean -dxf`).

## Steps

1. Check that `git tag --list 6.17.0` is empty.
2. `Directory.Build.props`: `<Version>6.17.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.17.0 — <date>` section on top, in 6.16.0's shape:
   - a summary list;
   - one section per change: the dialog focus, red questions, the badge menus;
   - *Upgrading from 6.16*, *Dependencies*, *Version*.

   Every path, file and number it quotes (MSI name, stored-format versions) is checked against the files.
4. `README.md`: the callout names 6.17 and sums up 6.16 after it — without the badge-menu clause, which
   6.17 takes back; 6.15's sentence goes, as 6.14's did in 6.16.0. The older upgrade notes stay.
5. `SECURITY.md`: 6.17.x supported; 6.16.x no longer.
6. `docs/RELEASE.md`: any line naming the current version is brought to 6.17.0; history stays.
7. Dependency check (`dotnet list Enigma.GitClient.slnx package --outdated`), logged. Non-coupled patch
   and minor bumps are applied; the Avalonia set is held unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release:
   - a clean build (`--no-incremental`) with zero warnings, the Debug build too;
   - the whole suite against the baseline, no new failure;
   - the classes that read the version (`ProductInformationTests`, `HostHttpTests`,
     `AboutDialogTests`) run on their own and pass;
   - the built Core and Desktop assemblies carry `6.17.0`;
   - `buildmsi.ps1`'s version resolution, re-done read-only, gives `Enigma.GitClient.6.17.0.msi`.

## Acceptance criteria

- The Release build is clean with zero warnings; the whole suite shows no failure the baseline lacks.
- The built assemblies carry 6.17.0, and the version-reading test classes pass.
- `RELEASENOTES.md` covers FEATURE-0A3D and FEATURE-83E5, says the badge menus' line items are gone
  again, and its quoted paths and numbers match the files.
- The README callout names 6.17. `SECURITY.md` supports 6.17.x only.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack`, `buildmsi`, or installs
  an MSI.

## Out of scope

- NuGet packaging; building the MSI; moving the Avalonia set on its own.
- The pre-existing Windows test-infrastructure failures (`TestServices.Dispose` and read-only git
  objects): a follow-up, already recorded.
- New README feature bullets: the feature devs' sweeps own the feature list.
- Merging into `develop`/`main` and tagging: the user's, after the run.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.17.0 | The user's rule, x.y+1.z with z = 0; new behaviour, no Core API change: a minor release | 6.16.1 (the focus rule is new behaviour, not a fix); 7.0.0 (nothing breaks) |
| What it carries | FEATURE-0A3D and FEATURE-83E5 | Exactly the commits since the `6.16.0` tag | Waiting for more items |
| The badge menus taken back | A section of their own, and in the summary list | Whoever read the 6.16.0 notes expects the line's items on the badge; FEATURE-83E5 left the statement to this release | Silence (the 6.16 notes would mislead); editing the 6.16.0 notes (history) |
| README callout | 6.17, then 6.16 without its badge-menu clause; 6.15's sentence goes | House shape: the newest two releases, then the older upgrade notes; the clause is no longer true | Keeping 6.15's sentence; quoting 6.16 verbatim |
| README feature list | Unchanged | Releases touch the callout only; the features' own sweeps found nothing stale | A bullet on dialog focus (gold-plating a release) |
| Shape of notes, callout, tag | As 6.16.0 | House convention | A single *Changes* list |
| Installer | No edit to `buildmsi`; the `<Version>` bump is what it builds | It reads `Directory.Build.props` | Hard-coding the version in the script |
| Dependencies | Non-coupled patch/minor bumps applied; the Avalonia set held with the library | House rule from earlier releases; on this machine the Desktop suite cannot run to its end, so a framework move would go unverified | Bumping everything |
| Pre-flight configuration | Release, baseline in Release too; the Debug build checked for warnings | `docs/RELEASE.md` pre-flights in Release; a baseline is only comparable in the same configuration | Debug only, as the feature devs ran |
| Tests on Windows | The whole suite against the baseline, plus the version-reading classes on their own | The host crash is pre-existing; the version string is the only code-facing change | Fixing the test infrastructure here (out of scope) |
| Phasing | One phase | A routine release (`dotnet-release`) | Splitting docs from the version |
