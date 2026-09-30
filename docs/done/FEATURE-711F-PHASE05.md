# FEATURE-711F-PHASE05 — A theme button beside About

**Item:** FEATURE-711F — Repository lists per profile
**Branch:** `feature/feature-711f-phase05-start-theme-button`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

The start window's toolbar has the repository window's theme switch, just left of About (after the
separator): the `Palette` icon and the tooltip "Switch between the dark and light themes". It
switches the whole application to the variant not on screen and records it as the Theme preference,
so the next start opens on it (the BUG-449E behaviour).

The switch itself moved out of `MainWindowViewModel` into a shared singleton, `IThemeSwitcher`
(`Services/ThemeSwitcher.cs`). Both windows' commands call it, so "switch and remember" exists once.

## Files / modules touched

**Created — App**

- `Services/ThemeSwitcher.cs` — `IThemeSwitcher`, `ThemeSwitcher` (the former `OnToggleTheme`, word for
  word)

**Modified — App**

- `ViewModels/MainWindowViewModel.cs` — takes `IThemeSwitcher` instead of `ISettingsService` (the
  settings were only there for the theme); `ToggleThemeCommand` calls it; `OnToggleTheme` and three
  usings removed
- `ViewModels/Pages/RepositoriesPageViewModel.cs` — takes `IThemeSwitcher`; `ToggleThemeCommand`
- `Views/Pages/RepositoriesPageView.axaml` — the `ToggleTheme` button between the separator and
  `OpenAbout`
- `DependencyInjection/ServiceCollectionExtensions.cs` — `IThemeSwitcher` registered as a singleton

**Modified — tests**

- `RepositoriesPageTests.cs` — the start page's switch flips the variant both ways and records Light,
  then Dark; the rendered button is bound to the command, named, the child just before About in the
  strip, and drawn left of it
- The three existing `MainWindowShellTests` theme tests pass unchanged through the shared service

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-711F.md` (the phase and the item are `DONE`)

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the button goes | Between the separator and About | "At the left of the info button", in the same group as About, as the repository toolbar has it |
| `MainWindowViewModel`'s settings dependency | Replaced by `IThemeSwitcher` | It was only used for the theme; keeping both would be a dead dependency |
| The icon and wording | The repository toolbar's (`Palette`, "Switch between the dark and light themes") | The same control in both windows |

## Deviations & follow-ups

- **None from the plan.**

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2592 passed, 0 failed (+2). Green on the first run.
- Documentation sweep: nothing to change. The README does not say where the theme switch is.
