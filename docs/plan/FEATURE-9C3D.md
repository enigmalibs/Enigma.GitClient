# FEATURE-9C3D — Release 6.19.0

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-9c3d-release-6-19-0`
**Run:** feature/2026-11-10-release-6-19-0

## Objective

Cut **6.19.0**, with FEATURE-092B (6.18.0) as the template, following the `dotnet-release` skill.
The release covers:

- the version;
- the release notes for what 6.19.0 adds beyond 6.18.0;
- the README callout, and any README line the release's features left for it;
- `SECURITY.md`;
- `docs/RELEASE.md`, where it names a version;
- a dependency check;
- a Release pre-flight.

## Context & constraints

- **An app, not a package.** `docs/RELEASE.md` is the runbook. A release is a bare `X.Y.Z` tag on
  `main`. The version lives in `Directory.Build.props` (`6.18.0` today). `buildmsi.ps1` reads it from
  there and has no version of its own.
- **The version rule is the user's:** `x.y.z` → `x.(y+1).z` → 6.19.0 (z is 0).
- **6.19.0 carries** what was built since the `6.18.0` tag (`git log 6.18.0..HEAD`), all `DONE`:
  - FEATURE-6693 (PHASE01–07), submodule support:
    - the *Submodules* dialog;
    - submodules follow the commits the repository expects, with a setting
      (`UpdateSubmodulesAutomatically`, on by default), a ticked clone box and a band;
    - a window on a submodule, with its mark and the no-branch band;
    - pin changes: the diff, the working-changes badge, the commit guard and *Discard*;
    - the push guard (`--recurse-submodules=check`, the offer to push the submodule first);
    - pin conflicts on the conflict page;
  - BUG-FACC: the startup crash (a DI cycle) that FEATURE-6693 PHASE06 brought in. It never shipped,
    so it is not a user-facing fix (`docs/done/BUG-FACC.md`).
- **The starting point is not on `develop` yet.** This run is cut from
  `bugfix/2026-11-10-startup-di-cycle` @ `b124232`, which stacks on the FEATURE-6693 run. Merging this
  run into `develop` brings both.
- **The README already describes submodules:** PHASE02–07 wrote its *Features* bullets. The release
  checks them against the code. It does not rewrite them.
- **Stored formats:** `AppSettings.CurrentVersion` is still 7. `UpdateSubmodulesAutomatically` is a
  new key, and a file without it reads `true`. The other stores are unchanged:
  `RepositoryListStore` 1, `HostAccountService` 2, `TokenStore` 1, `IdentityProfileStore` 1,
  `HiddenBranchStore` 1.
- **The setting applies without a restart**: `SubmoduleFollowStep` and `SubmoduleOperations` read
  `_settings.Current` when they run.
- `SECURITY.md` supports only the latest minor line.
- The Avalonia set moves only as a whole, and only with Enigma.Avalonia.Desktop. In 6.18.0 it was
  held at 12.1.4, with Enigma.Avalonia.Desktop 1.3.0.
- **This machine is Windows.** Never run `Enigma.GitClient.Desktop.UnitTests`, in whole or in part:
  it is the machine's standing rule (BUG-6EAA). The Desktop suite is compiled, not run. One Core
  failure is known on Windows only: `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain`.
- Never tag, push or merge into `develop`/`main` inside the run. Never run `buildmsi.ps1`: it starts
  with `git clean -dxf`.

## Steps

1. Check that `git tag --list 6.19.0` is empty.
2. `Directory.Build.props`: `<Version>6.19.0</Version>`.
3. `RELEASENOTES.md`: a dated `## 6.19.0 — <date>` section on top, in 6.18.0's shape:
   - a summary list;
   - one section per user-facing change:
     - the *Submodules* dialog;
     - submodules that follow: clone, the follow step, the setting, the band, worktrees;
     - a window on a submodule;
     - a submodule's change: the diff, the working changes, the commit question, *Discard*;
     - the push;
     - conflicts;
   - *Upgrading from 6.18*: the MSI and `install.sh`, the stored formats and going back, and what now
     behaves differently:
     - submodules move on their own after the app's operations;
     - the clone box starts ticked;
     - a push checks the submodules' commits, whatever `push.recurseSubmodules` says;
     - `submodule.recurse` no longer moves submodules during the app's own operations;
   - *Dependencies*, *Version*.

   Every path, file, menu text, limit and number the notes quote is checked against the files:
   button and menu texts, band texts, the setting's label, the stored-format versions and the MSI
   name.
4. `README.md`:
   - the callout names 6.19 and sums up 6.18 after it. 6.17's sentence goes, as 6.16's did in 6.18.0.
     The older upgrade notes stay;
   - the *Preferences that stick* bullet gains whether submodules follow;
   - the submodule *Features* bullets are checked against the code, and fixed only where they are
     wrong.
5. `SECURITY.md`: 6.19.x supported; 6.18.x no longer.
6. `docs/RELEASE.md`: any line naming the current version is brought to 6.19.0. History stays.
7. Dependency check (`dotnet list Enigma.GitClient.slnx package --outdated`), logged:
   - non-coupled patch and minor bumps are applied;
   - the Avalonia set is held unless Enigma.Avalonia.Desktop moved with it.
8. Pre-flight in Release, on Windows:
   - a clean build (`--no-incremental`) with zero warnings, the Debug build too. The Desktop tests
     compile;
   - the Core unit and integration suites against the baseline, with no new failure;
   - the Core classes that read the version (`ProductInformationTests`, `HostHttpTests`,
     `DisplayNameConsistencyTests`) run on their own and pass. `AboutDialogTests` is compiled, not run;
   - a throwaway harness in the session scratchpad, outside the repository, builds the container as
     `CompositionRootTests.BuildProvider` does (`ValidateOnBuild`, `ValidateScopes`) against the
     Release build, and resolves the startup services. It stands in for the Desktop suite's
     composition test, which cannot run here;
   - the built Core and Desktop assemblies carry `6.19.0`;
   - `buildmsi.ps1`'s version lookup, replayed read-only, gives `Enigma.GitClient.6.19.0.msi`.

## Acceptance criteria

- The Release and Debug builds are clean, with zero warnings.
- The Core suites show no failure the baseline lacks.
- The Desktop suite compiles. It is reported as not run.
- The container harness reports `ValidateOnBuild: OK`, and every startup service resolves.
- The built assemblies carry 6.19.0, and the Core version-reading test classes pass.
- `RELEASENOTES.md` covers FEATURE-6693, with *Upgrading from 6.18* naming the four behaviour
  changes. Its quoted paths, texts and numbers match the files.
- The README callout names 6.19. The *Preferences* bullet names the submodule setting.
- `SECURITY.md` supports 6.19.x only.
- Nothing in the dev runs `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or
  installs an MSI.

## Out of scope

- NuGet packaging, building the MSI, and moving the Avalonia set on its own.
- Running the Desktop suite: the user runs it on Linux before tagging (`docs/done/BUG-FACC.md`'s
  follow-up).
- Making the Desktop suite runnable on Windows (BUG-6EAA).
- Merging into `develop`/`main` and tagging: the user does these after the run.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Version number | 6.19.0 | The user's rule, x.y+1.z with z = 0. Submodule support is new behaviour and breaks nothing: a minor release | 6.18.1 (submodule support is a feature, not a fix); 7.0.0 (nothing breaks) |
| What it carries | FEATURE-6693, plus BUG-FACC's fix, which the notes leave out | Exactly the commits since the `6.18.0` tag. BUG-FACC fixed a crash that no release had | Listing BUG-FACC under *Fixes* (a user never met it); waiting for more items |
| Starting point | The current `HEAD`, `b124232`, not yet on `develop` | The run rule is to cut from `HEAD`. The release must carry FEATURE-6693, which only this line holds | Waiting for the user to merge the earlier runs first |
| README callout | 6.19, then 6.18; 6.17's sentence goes | House shape: the newest two releases, then the older upgrade notes | Keeping 6.17's sentence |
| README body | *Preferences that stick* gains the submodule setting. The submodule bullets are checked, not rewritten | The bullet lists every setting, and this one also applies without a restart. PHASE02–07 already wrote the features | Callout only (leaves the settings list incomplete); rewriting the feature bullets (gold-plating) |
| Behaviour changes | Named in *Upgrading from 6.18*: automatic follow, the ticked clone box, the push check, `submodule.recurse` ignored | Each one changes what an existing user sees, without being asked | A *Breaking changes* section (none is breaking for an app); leaving them to the feature sections |
| Shape of the notes, callout and tag | As 6.18.0 | House convention | A single *Changes* list |
| Dependencies | Non-coupled patch and minor bumps applied. The Avalonia set held unless Enigma.Avalonia.Desktop moved | House rule from earlier releases | Bumping everything |
| Pre-flight configuration | Release, with the baseline in Release too. The Debug build checked for warnings | `docs/RELEASE.md` pre-flights in Release, and a baseline compares only in the same configuration | Debug only |
| Desktop suite on Windows | Compiled, not run. A container-validation harness covers the startup composition. Linux run before tagging is a follow-up | The machine's standing rule. The DI crash got through exactly because `CompositionRootTests` never ran | Running the suite (forbidden); skipping a startup check (the gap that let BUG-FACC through) |
| Installer | No edit to `buildmsi`. Its version lookup is replayed read-only | It reads `Directory.Build.props` | Hard-coding the version; running the script (`git clean -dxf`) |
| Phasing | One phase | A routine release (`dotnet-release`) | Splitting the docs from the version |
