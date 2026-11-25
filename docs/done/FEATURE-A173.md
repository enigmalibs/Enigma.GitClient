# FEATURE-A173 — Release 6.20.0

**Item:** FEATURE-A173 — Release 6.20.0
**Branch:** `feature/feature-a173-release-6-20-0`
**Run:** feature/2026-11-25-release-6-20-0

## Summary

Cut **6.20.0** in 6.19.0's shape:

- the version in `Directory.Build.props` is 6.20.0. `buildmsi.ps1` reads it from there, so the
  installer it builds is now `artifacts\Enigma.GitClient.6.20.0.msi`;
- a dated `## 6.20.0 — 2026-11-25` section is on top of `RELEASENOTES.md`. It has one section per
  change a user meets (programs by their full path, files that would run, the accounts and tokens, the
  log file, conflicts, creating and cloning, the light theme), *Under the hood* for the review's
  structural work, and *Upgrading from 6.19* with the three behaviour changes;
- the README callout names 6.20 and sums up 6.19 after it; 6.18's sentence goes;
- `SECURITY.md` supports 6.20.x only;
- Microsoft.Extensions.Hosting, Microsoft.Extensions.Http and System.Security.Cryptography.ProtectedData
  are 10.0.15, the .NET 10 servicing patch.

The release carries everything built since the `6.19.0` tag, all `DONE`: CODE-REVIEW-6E56, the fixes
for the 2026-11-19 solution review (27 phases, PHASE05 abandoned), and the review report itself.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `Directory.Packages.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-A173.md`: statuses.
- Created: `docs/done/FEATURE-A173.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Microsoft.Extensions.* and ProtectedData 10.0.14 → 10.0.15 | Applied, the three together | A servicing patch, not in the coupled list (`dotnet-solution-config`). The three share the .NET 10 train, so they stay on one version |
| Their 11.0.0 | Not taken | A major, on the .NET 11 train; the app targets `net10.0` |
| Avalonia 12.2.0 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| `docs/RELEASE.md` | Unchanged | It names no current version: `1.1.0` is an example, and 6.13.0 is the MSI's history |
| README body | Unchanged, after a check | PHASE13 already wrote the `logs/` row and the `appsettings.json` paragraph. Nothing else in *Features*, *Where your things are kept* or *Requirements* became wrong |
| Notes on macOS | Left out | The README says the app runs on Linux and Windows. `RunnableFiles`' non-Windows list is described as Linux's |
| The five Desktop failures | Recorded here and in the run's report, not fixed | The plan's decision: a release commit changes no product code |

## Deviations & follow-ups

- **Five Desktop tests fail, in the baseline and in this dev alike.** The Desktop suite ran here for the
  first time since CODE-REVIEW-6E56, whose Desktop tests were only ever compiled. **Fix these before
  tagging 6.20.0** (a BUG item):
  - `DialogTargetValidationTests.AFieldProblem_IsSaidAtOnce_AndNoProbeRuns`: one probe runs where
    none should (expected 0, actual 1). New in CODE-REVIEW-6E56 PHASE14.
  - `RepositoryWatcherTests.AWatchTheSystemRefuses_CrashesNothing_RefreshesNothing_AndWarnsOnce`: the
    test reads "The file-system watcher started" where it expects the `max_user_watches` warning.
    PHASE12 added that start log line. The test passes at `6.19.0`.
  - `HistoryCommandBarTests.TheCommands_AreInTheOrderAskedFor_InFiveGroups`,
    `TheGroups_AreSetApart_ByTheRoomAroundTheirSeparators`,
    `OnANarrowWindow_TheCommandsGiveUpTheirNames_AndStayOnTheBar`: a command-bar button has no label
    the test can find, and a separator's gap measures −566 where 2 is expected. **Already failing at
    `6.19.0`** (the suite run on a scratch copy of the tag), so they shipped in 6.19.0.
  - At `6.19.0`, `RepositoriesPageTests.ARepositoryOpenedWhileAProfileIsPicked_JoinsThatProfilesList`
    failed too. It passes now.
- **After merging:** run `packaging\windows\buildmsi.ps1` on `main`, on Windows, to build
  `artifacts\Enigma.GitClient.6.20.0.msi`, then install it over 6.19.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script, because it starts with `git clean -dxf`.
- Merging into `develop` and `main`, tagging `6.20.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.
- Four Core unit tests are skipped on Linux (Windows-only), in the baseline and here alike.

## Build/test evidence

- `git tag --list 6.20.0`: empty (6.19.0 is the latest tag).
- **Baseline**, taken once for the run, in Release, on Linux: the run branch at `6414b31` (the
  starting `develop` @ `ee420c7` plus the planning commit).
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --project tests/Enigma.GitClient.Core.UnitTests -c Release`: 1534 run, 1530 passed,
    0 failed, 4 skipped.
  - `dotnet test --project tests/Enigma.GitClient.Core.IntegrationTests -c Release`: 449 run, 449
    passed, 0 failed.
  - `dotnet test --project tests/Enigma.GitClient.Desktop.UnitTests -c Release`: 2020 run, 2015
    passed, **5 failed** (the five above).
- **This dev**, same commands:
  - Release build: **0 warnings, 0 errors**. The Debug build (`--no-incremental`): 0 warnings,
    0 errors.
  - Core unit: 1534 run, 1530 passed, 0 failed, 4 skipped.
  - Core integration: 449 run, 449 passed, 0 failed.
  - Desktop: 2020 run, 2015 passed, 5 failed, the baseline's five and no other.
  - The version-reading classes (`ProductInformationTests`, `HostHttpTests`,
    `DisplayNameConsistencyTests`, `AboutDialogTests`) ran in those suites, with no failure.
  - The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` (Release) carry
    `6.20.0+6414b31598c5c518fb5ddba70b5222ae37193bde`, file version 6.20.0.0.
- **`6.19.0`, for comparison** (a scratch copy of the tag, Release): the Desktop suite ran 1906, 1902
  passed, 4 failed (the three `HistoryCommandBarTests` and `RepositoriesPageTests` above).
- `buildmsi.ps1`'s version lookup, replayed read-only with its XPath (`/Project/PropertyGroup/Version`)
  and pattern (`^\d+\.\d+\.\d+$`): `version = 6.20.0`, `msi = artifacts\Enigma.GitClient.6.20.0.msi`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-25 (and `--highest-minor`):
  - Microsoft.Extensions.Hosting, Microsoft.Extensions.Http, System.Security.Cryptography.ProtectedData
    10.0.14 → 10.0.15 applied; 11.0.0 not taken;
  - the Avalonia set (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent,
    Avalonia.Headless, Avalonia.Skia) 12.1.4 → 12.2.0: held;
  - nothing else outdated.
- **The notes were checked against the files:**
  - stored formats: no `CurrentVersion` changed since `6.19.0` (`git diff 6.19.0..HEAD`);
  - the backup name `host-accounts.corrupt-<yyyyMMdd-HHmmss>.json` (`HostAccountService.FileName`,
    the UTC stamp);
  - the log: `logs` under the configuration directory, `enigma-gitclient-<yyyyMMdd>.log`, 7 days
    (`FileLoggerProvider.RetainedDays`), the dialog's sentence (`UnhandledErrorViewModel`);
  - the program folders and the `PATH` rule (`SystemInterop.Locate`, `ExecutableSearch`), *Path to
    git* (`SettingsPageView.axaml`), the *git is required* title (`AppWindows`);
  - *Open file* / *Show in file manager* and the extension lists (`ChangedFilesPanelView.axaml`,
    `RunnableFiles`);
  - "N unchanged lines", "1 unchanged line" (`ConflictResolutionPageViewModel`), Settings → Diff →
    *Context lines*, 3 by default (`AppSettings.DiffContextLines`);
  - the 250 ms delay (`DebouncedProbe.Delay`), the *Create* / *Clone* buttons
    (`RepositoriesPageViewModel`), "'…' exists but cannot be read." (clone only,
    `CloneRepositoryDialogViewModel`);
  - the theme colours (`Graph.axaml`: `AuthorAvatarInitialsColor` white on the light variant);
  - Enigma.Msi 1.7.0 and WixSharp_wix4 2.14.2 (`packaging/windows/Enigma.Msi.Worker/README.md`),
    unchanged since 6.19.0.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles.

## Documentation sweep

- The release documents are this dev's subject, and the README edit above is its own. There is no
  `CLAUDE.md`, `AGENTS.md`, `CONTRIBUTING.md` or `CHANGELOG.md`. No further edit.
