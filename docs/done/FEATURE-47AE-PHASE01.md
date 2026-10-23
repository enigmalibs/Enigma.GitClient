# FEATURE-47AE-PHASE01 — No theme switch nor About on the bar

**Item:** FEATURE-47AE — A lighter History command bar, PHASE01 — No theme switch nor About on the bar
**Branch:** `feature/feature-47ae-phase01-no-theme-about`
**Run:** feature/2026-10-23-history-bar-polish

## Summary

The History page's command bar no longer carries the theme switch and About. Both are on the Settings
page: the theme preference, and its About button. The bar's right part is now the search block alone.

- **The view.**
  - `CommandBarEnd` lost its separator, `SwitchTheme` and `OpenAbout`.
  - Its nested grid is flattened into one `*,Auto,Auto,Auto,Auto` grid: the box, the counter, and the
    three search buttons.
  - The clear button is named `ClearSearch`, so tests can find the end of the bar.
  - Its widths (`MinWidth` 360, `MaxWidth` 600) are unchanged in this phase. PHASE02 retunes them.
- **The window's view model.** Nothing binds `ToggleThemeCommand` and `OpenAboutCommand` any more, so
  they are removed from `MainWindowViewModel`, with its `IThemeSwitcher` and `IAboutDialogService`
  dependencies. The container builds the view model, so no registration changed.
- **The start window is unchanged.** It keeps its theme switch and About over the same services. The
  comments that said "both windows" or "as the repository window's toolbar does" now describe the
  start window alone.

## Files / modules touched

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/ThemeSwitcherTests.cs`: the three tests that drove the
  window's `ToggleThemeCommand`, now on `IThemeSwitcher`. They check that it switches the whole
  application, that it records the preference (which the Settings page shows), and that the preference
  survives a restart.
- `docs/done/FEATURE-47AE-PHASE01.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Views/Pages/HistoryPageView.axaml`: the end of the bar, and its comments.
- `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`: the two commands, the two fields,
  the two constructor parameters and their XML docs removed.
- `src/Enigma.GitClient.Desktop/Services/ThemeSwitcher.cs`, `ViewModels/Pages/RepositoriesPageViewModel.cs`,
  `Views/Pages/RepositoriesPageView.axaml`, `Themes/Styles.axaml` (the `:compact` comment): comments only.
- `tests/Enigma.GitClient.Desktop.UnitTests/HistoryCommandBarTests.cs`:
  - The window's commands no longer list theme and About.
  - The centre test is renamed `TheCommands_AreAtTheBarsCentre_WithSearchOnItsRight`. Clearing the
    search ends the bar, at toolbar size.
  - The narrow-window test is renamed `…_AndTheSearchStaysOnTheBar` and checks the search block's last
    button.
  - New: `TheBar_HasNeitherTheThemeSwitchNorAbout`. No button named `SwitchTheme` or `OpenAbout`, and no
    `Palette` or `Info` icon, is on the bar.
- `tests/Enigma.GitClient.Desktop.UnitTests/ToolbarButtonLookTests.cs`: `TheCommandBar_FramesWhat…`
  took the theme switch as the bar's always-enabled button. It now takes *Close the repository*.
- `tests/Enigma.GitClient.Desktop.UnitTests/MainWindowShellTests.cs`: the three `ToggleThemeCommand_*`
  tests moved out, and three usings that only they needed were removed.
- `tests/Enigma.GitClient.Desktop.UnitTests/AboutDialogTests.cs`: `TheRepositoryToolbar_OpensIt` and
  its now-unused using removed. The repository window reaches About through Settings, which
  `TheSettingsPage_OpensIt_…` covers.
- `docs/roadmap.md`, `docs/plan/FEATURE-47AE.md`: statuses.

## Deviations & follow-ups

- **Not in the plan's test list:** `ToolbarButtonLookTests.TheCommandBar_FramesWhatCanBePressedAndGreysWhatCannot`
  used the theme switch. It was red on the first run and fixed in cycle 1 (see *Build/test evidence*).
- **An intermediate state, fixed by PHASE02:** with theme and About gone and the end's `MaxWidth` still
  600, the search box is wider on a wide bar (about 426 px, from about 345) until PHASE02 narrows it.
- Documentation sweep:
  - The README never placed the theme switch or About on the History bar, so it needed no change.
  - `RELEASENOTES.md` is written by the release item.
  - Nothing was edited.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The end's nested grid | Flattened into one grid | Its second column only held theme and About; one grid says the same with less |
| How tests find the bar's last button | Name the clear button `ClearSearch` | The other two search buttons are named; a name is steadier than a position |
| The bar's always-enabled button in the look test | *Close the repository* | Its command has no condition, so it is enabled with no repository open, as the theme switch was |
| The moved theme tests | Kept whole, on `IThemeSwitcher` | The service is still what the start window's switch runs; the restart and Settings-page checks are not covered elsewhere |

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- First run: 3453 total, 1 failed. `ToolbarButtonLookTests.TheCommandBar_FramesWhatCanBePressedAndGreysWhatCannot`
  could not find the theme switch.
- **Cycle 1** (that test takes *Close the repository*): `dotnet test --solution Enigma.GitClient.slnx`
  gave 3453 total, 3450 passed, 3 skipped, 0 failed.
  - Same count as the baseline: one About test removed, `TheBar_HasNeitherTheThemeSwitchNorAbout`
    added, three theme tests moved.
- Unused usings checked with `dotnet format style --diagnostics IDE0005` on the touched files; the
  three it reported are removed.
