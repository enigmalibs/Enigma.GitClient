# FEATURE-9C3D — Release 6.19.0

**Item:** FEATURE-9C3D — Release 6.19.0
**Branch:** `feature/feature-9c3d-release-6-19-0`
**Run:** feature/2026-11-10-release-6-19-0

## Summary

Cut **6.19.0** in 6.18.0's shape:

- the version in `Directory.Build.props` is 6.19.0. `buildmsi.ps1` reads it from there, so the
  installer it builds is now `artifacts\Enigma.GitClient.6.19.0.msi`;
- a dated `## 6.19.0 — 2026-11-10` section is on top of `RELEASENOTES.md`. *Upgrading from 6.18* names
  the four things that now happen in a repository with submodules without being asked;
- the README callout names 6.19 and sums up 6.18 after it. The *Preferences that stick* bullet names
  the submodule setting;
- `SECURITY.md` supports 6.19.x only;
- xunit.v3 is 4.0.3, for the tests only.

The release carries everything built since the `6.18.0` tag, all `DONE`:

- FEATURE-6693 (PHASE01–07): submodule support;
- BUG-FACC: the startup crash that FEATURE-6693 PHASE06 brought in. No release ever had the crash, so
  the notes leave it out (`docs/done/BUG-FACC.md`).

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `Directory.Packages.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-9C3D.md`: statuses.
- Created: `docs/done/FEATURE-9C3D.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.6 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| xunit.v3 4.0.2 → 4.0.3 | Applied | A non-coupled patch bump. It is used by the tests only, so the application's dependencies do not move |
| `docs/RELEASE.md` | Unchanged | It names no current version: `1.1.0` is an example, and 6.13.0 is the MSI's history |
| The README's submodule bullets | Unchanged, after a check against the code | PHASE02–07 wrote them, and every claim still holds: the dialog, the follow step and its setting, the band, the window, pin changes, the push, conflicts |
| Going back to 6.18, in the notes | Says that 6.18 drops the new key when it saves | `SettingsService` reads with the default `JsonSerializerOptions`, which skip unknown keys. 6.18 therefore reads a 6.19 file, but writes it back without `updateSubmodulesAutomatically`, which 6.19 then reads as on |
| How the notes describe the Core change | "Core gains the `Submodules` namespace, and `ConflictService` takes the submodule service" | `git diff 6.18.0..HEAD` on Core removes no public member. The one signature change is `ConflictService`'s constructor, which the container supplies |
| The dev branch's base after someone else moved `HEAD` (see *Deviations*) | `git merge --ff-only` of the run branch into the empty dev branch | It is not destructive: the branch had no commit of its own, and `9f419a0` is an ancestor of the run tip. The alternatives were `reset --hard` / `branch -f` (forbidden) or a second dev branch with another slug (a stray branch, and a name that no longer matches the plan) |

## Deviations & follow-ups

- **Someone else moved this working tree's `HEAD` during the run.** The reflog shows, between 11:41:12
  and 11:41:44 on 2026-11-10, while the baseline integration suite ran:
  - `checkout: moving from feature/2026-11-10-release-6-19-0 to develop`;
  - `merge feature/2026-11-10-release-6-19-0` into `develop`;
  - `Reset to origin/develop`.

  The run ran none of these. It does not write to `develop`. `develop` is now `origin/develop`
  (`9f419a0`). Before that it was at `6aacbf3`, which the FEATURE-6693 branches still hold. As a
  result, the dev branch was first cut from `develop`, not from the run tip `8783d34`. Nothing had
  been written on it: `roadmap.py` refused, because the row was not there. It was fast-forwarded to
  `8783d34`, then built as planned. The baseline's test binaries had been built before the switch, so
  the baseline stands.
- **After merging:** run the Desktop suite **on Linux** before tagging (`docs/done/BUG-FACC.md`'s
  follow-up). It is not run on this machine, and it holds `AboutDialogTests` and
  `CompositionRootTests`. The harness below checks the same container validation, and nothing more.
- **After merging:** run `packaging\windows\buildmsi.ps1` on `main`, on Windows, to build
  `artifacts\Enigma.GitClient.6.19.0.msi`, then install it over 6.18.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script, because it starts with `git clean -dxf`.
- Merging into `develop` and `main`, tagging `6.19.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them. This run stacks on `bugfix/2026-11-10-startup-di-cycle` and
  `feature/2026-11-09-feature-6693`, so merging it into `develop` brings FEATURE-6693 and BUG-FACC as
  well.
- Pre-existing, left alone:
  - `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` fails on Windows
    (DPAPI) and passes on Linux;
  - README *Where your things are kept* lists the git configuration writes for identities only.
    Setting an upstream, managing remotes and now `submodule update --init` (which records
    `submodule.<name>.url` in the repository's `.git/config`) write it too. The sentence was already
    narrower than the app before this release.

## Build/test evidence

- `git tag --list 6.19.0`: empty (6.18.0 is the latest tag).
- **Baseline**, taken once for the run, in Release, on Windows. It is the run branch at `8783d34`:
  the starting `bugfix/2026-11-10-startup-di-cycle` @ `b124232`, plus the planning commit.
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release`: 1508 run, 1505 passed,
    **1 failed** (the Windows-only `TokenStoreTests` one above), 2 skipped.
  - `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release`: 449 run, 447
    passed, 0 failed, 2 skipped (9 min 03 s).
  - `Enigma.GitClient.Desktop.UnitTests`: compiled, **not run** (this machine's standing rule).
  - The container harness (below): `ValidateOnBuild: OK`, six services resolved.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**. The
    Debug build too. The Desktop tests compile.
  - Core unit, Release: 1508 run, 1505 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration, Release: 449 run, 447 passed, **0 failed**, 2 skipped (9 min 59 s).
  - Desktop: compiled, **not run**. `AboutDialogTests` is among them.
  - The Core version-reading classes on their own: `ProductInformationTests`, `HostHttpTests` and
    `DisplayNameConsistencyTests`, 28/28 passed.
  - The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` (Release) carry
    `6.19.0+8783d346e436efecf6aa22879edceeff04a65af6`, file version 6.19.0.0.
  - **Container harness:** a throwaway console in the session scratchpad, outside the repository. It
    loads the Release `Enigma.GitClient.Desktop.dll`, calls `App.ConfigureServices` as
    `CompositionRootTests.BuildProvider` does, and builds with `ValidateOnBuild` and `ValidateScopes`.
    It reports `ValidateOnBuild: OK` and resolves `IUnhandledErrorReporter`, `IAppWindows`,
    `IInstanceLauncher`, `ISyncOperations`, `ISubmoduleOperations` and `ISubmoduleFollowStep`.
    Exit code 0.
- `buildmsi.ps1`'s version lookup, replayed read-only with its XPath (`/Project/PropertyGroup/Version`)
  and pattern (`^\d+\.\d+\.\d+$`): `version = 6.19.0`, `msi = artifacts\Enigma.GitClient.6.19.0.msi`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-10:
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent,
    Avalonia.Headless, Avalonia.Skia) 12.1.4 → 12.1.6: held, as above;
  - xunit.v3 4.0.2 → 4.0.3: applied;
  - Core: no updates.
- **The notes were checked against the files:**
  - stored formats: `AppSettings.CurrentVersion` 7, `RepositoryListStore.CurrentVersion` 1,
    `HostAccountService.CurrentVersion` 2, `TokenStore.CurrentVersion` 1, as in 6.18.0. The new key
    is `updateSubmodulesAutomatically` (camel case, `SettingsService`), default `true`. The setting
    is read from `_settings.Current` when it is used, so it applies at once;
  - texts:
    - the dialog's states and menu (`SubmodulesPageViewModel`, `SubmodulesPageView.axaml`);
    - the band (`SubmoduleNoticeViewModel`), the window's mark and band (`SubmoduleWindowViewModel`);
    - the pin summary and "and N more" (`PinChange`), the working-changes words
      (`ChangedFilesPanelViewModel`);
    - the commit question and *Commit anyway* (`WorkingTreePanelViewModel`);
    - the push offer (`SyncOperations`), the conflict cards and *Take ours* / *Take theirs*
      (`ConflictResolutionPageViewModel`), the terminal sentence (`ConflictService`);
    - *Clone submodules as well* under *More options* (`CloneRepositoryDialogView.axaml`, ticked by
      default in `CloneRepositoryDialogViewModel`);
    - *Update submodules automatically* in Settings → Git, under *Pull* (`SettingsPageView.axaml`);
  - `submodule.recurse=false` is added to checkout, switch, pull, merge, reset, revert, cherry-pick,
    restore and read-tree (`GitCommandFactory`, `docs/done/FEATURE-6693-PHASE03.md`);
    `--recurse-submodules=check` is passed on every push but a delete, the forced and tag pushes
    included (`docs/done/FEATURE-6693-PHASE06.md`);
  - paths: `packaging/windows/buildmsi.ps1`, `packaging/linux/install.sh`. Enigma.Msi 1.7.0 and
    WixSharp_wix4 2.14.2 (`packaging/windows/Enigma.Msi.Worker/README.md`), unchanged since 6.18.0.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles.

## Documentation sweep

- The release documents are this dev's subject, and the README edits above are its own. There is no
  `CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING.md` or `CHANGELOG.md`. No further edit.
