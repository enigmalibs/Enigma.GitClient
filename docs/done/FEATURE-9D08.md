# FEATURE-9D08 — Release 6.17.0

**Item:** FEATURE-9D08 — Release 6.17.0
**Branch:** `feature/feature-9d08-release-6-17-0`
**Run:** feature/2026-11-06-release-6-17-0

## Summary

Cut **6.17.0** in 6.16.0's shape:

- the version in `Directory.Build.props` is 6.17.0. `buildmsi.ps1` reads it from there, so the
  installer it builds is now `artifacts\Enigma.GitClient.6.17.0.msi`;
- a dated `## 6.17.0 — 2026-11-06` section is on top of `RELEASENOTES.md`;
- the README callout names 6.17 and sums up 6.16 after it;
- `SECURITY.md` supports 6.17.x only.

The release carries everything built since the `6.16.0` tag, all `DONE`:

- FEATURE-0A3D: every dialog opens with the focus on its first text field, else its default button;
  a red question opens on Cancel, and eight more questions are red;
- FEATURE-83E5: a branch or tag badge's menu holds the badge's own items again. The notes say the
  line's items 6.16.0 put there are gone, as its done doc asked.

## Files/modules touched

- Modified:
  - `Directory.Build.props`
  - `RELEASENOTES.md`
  - `README.md`
  - `SECURITY.md`
  - `docs/roadmap.md`, `docs/plan/FEATURE-9D08.md`: statuses.
- Created: `docs/done/FEATURE-9D08.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Avalonia 12.1.5 | Held at 12.1.4, logged in *Dependencies* | Enigma.Avalonia.Desktop 1.3.0, still the latest, is built against 12.1.4, and the set moves only with it |
| `docs/RELEASE.md` | Unchanged | It names no current version: `1.1.0` is an example, and 6.13.0 is the MSI's history |
| The 6.16 clause about badge menus | Left out of the callout's 6.16 summary; the 6.16.0 notes stay as written | 6.17 takes it back; the old notes are history |
| "Main" or "default" button | "Default button" in the notes and the callout | The rule follows each dialog's `DefaultButton`: the detach warning focuses its secondary button |
| Escape | "Enter and Escape reach it" | The library closes a dialog on Escape once a key reaches it (`ContentDialog.OnKeyDown`). The notes do not promise more than that |
| Which tool dialogs get a caret | The branches and the tags dialogs only | Only they have a filter box; the remotes and worktrees dialogs fall back to a button |

## Deviations & follow-ups

- **After merging:** run `packaging\windows\buildmsi.ps1` on `main`, on Windows, to build
  `artifacts\Enigma.GitClient.6.17.0.msi`, then install it over 6.16.0 (`docs/RELEASE.md` §6–§7). The
  run did not run the script: it starts with `git clean -dxf`.
- Merging into `develop` and `main`, tagging `6.17.0` and pushing are the user's (`docs/RELEASE.md`
  steps 2–4). Nothing here ran them.
- **Pre-existing on this Windows machine, untouched:**
  - The Desktop test host still crashes part-way: exit code `0xE0434352`, an `ObjectDisposedException`
    from `RepositoryContext.RunExclusiveAsync`.
  - 184 tests fail in the baseline as in this dev:
    - 181 cleanup `UnauthorizedAccessException`s (read-only git objects in `TestServices.Dispose`);
    - 2 cleanup `IOException`s (`settings.json.*.tmp` in use: `ProfileSelectionTests`,
      `BranchesPageTests.Page_DeletesAnUnmergedBranchOnlyAfterTheWarning`);
    - `TokenStoreTests.AfterTheResetOfAnUnreadableStore_SecretsAreStoredAndReadAgain` (DPAPI).

    The follow-up of `docs/done/FEATURE-0A3D-PHASE01.md` stands.
- The run's baseline is taken in Release. The earlier runs' baselines were in Debug: 2215 run and 35
  failed there, against 2452 run and 184 failed here, because the host crashes at a different point.

## Build/test evidence

- `git tag --list 6.17.0`: empty (6.16.0 is the latest tag).
- **Baseline**, the run branch at `0f536c8` (the starting `HEAD` `f4f1e31` plus the planning commit),
  taken once for the run, in Release:
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
  - `dotnet test --solution Enigma.GitClient.slnx -c Release`: 2452 run, 2264 passed, 184 failed,
    4 skipped. Every failure is listed under *Deviations & follow-ups*.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx -c Release --no-incremental`: **0 warnings, 0 errors**; the
    Debug build too.
  - `dotnet test --solution Enigma.GitClient.slnx -c Release`: 2452 run, 2264 passed, 184 failed,
    4 skipped. The failing names are exactly the baseline's; **none is new**.
  - The version-reading classes on their own, all passed:
    - Core: `ProductInformationTests`, `HostHttpTests`, `DisplayNameConsistencyTests`, 28/28;
    - Desktop: `AboutDialogTests`, 17/17.
  - The built `Enigma.GitClient.Core.dll` and `Enigma.GitClient.Desktop.dll` carry
    `6.17.0+0f536c8acd3aafefe1c54c21fa5604a3cc42c99a` (`FileVersion` 6.17.0.0).
- `buildmsi.ps1`'s version lookup, replayed read-only in PowerShell with the script's own XPath
  (`/Project/PropertyGroup/Version`) and pattern (`^\d+\.\d+\.\d+$`): `version = 6.17.0`,
  `msi = artifacts\Enigma.GitClient.6.17.0.msi`.
- `dotnet list Enigma.GitClient.slnx package --outdated`, 2026-11-06: only the Avalonia set
  (Avalonia, Avalonia.Desktop, Avalonia.Fonts.Inter, Avalonia.Themes.Fluent, Avalonia.Headless,
  Avalonia.Skia) 12.1.4 → 12.1.5; held, as above. Every other project: no updates.
- **The notes were checked against the files:**
  - stored formats: `AppSettings.CurrentVersion` 7, `RepositoryListStore.CurrentVersion` 1,
    `HostAccountService.CurrentVersion` 2, `TokenStore.CurrentVersion` 1, as in 6.16.0;
  - file names: `settings.json`, `repository-lists.json`, `host-accounts.json`, `tokens.json`;
  - paths: `packaging/windows/buildmsi.ps1`, `packaging/linux/install.sh`; Enigma.Msi 1.7.0 and
    WixSharp_wix4 2.14.2 (`packaging/windows/Enigma.Msi.Worker/README.md`);
  - texts and menus: the two identity questions' titles (`RepositoriesPageViewModel`), the badge menus'
    items (`HistoryPageView.axaml`).
- No Core source changed since `6.16.0`: no public Core API moves.
- Nothing in this dev ran `git tag`, `git push`, `git clean`, `dotnet pack` or `buildmsi`, or installed
  an MSI.
- Fix budget: 0 cycles.

## Documentation sweep

- The release documents are this dev's subject. README line 80 ("a branch from its badge's menu")
  stays true. The feature list says nothing about dialog focus, so nothing went stale. There is no
  `CLAUDE.md`. No further edit.
