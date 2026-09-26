# BUG-449E — Theme toggle is forgotten on restart

**Status:** DONE — see `docs/done/BUG-449E.md`
**Type:** BUG
**Branch:** `bugfix/bug-449e-theme-toggle-persist`
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Objective

The repository toolbar's "Switch between the dark and light themes" button switches the theme for the
session only: `MainWindowViewModel.OnToggleTheme` sets `Application.RequestedThemeVariant` and never
touches the settings, so the next start comes back on whatever the "Theme" preference says. The
button must record the theme it switched to as the preference, so the application starts on it next
time — and the settings page shows it.

## Context & constraints

- `AppSettings.Theme` (`ThemePreference.System | Dark | Light`) is the stored preference;
  `App.OnFrameworkInitializationCompleted` applies it before the first window and follows
  `ISettingsService.Changed` with `App.ApplyTheme`.
- `ISettingsService.Update` changes the settings, notifies every listener (the settings page included)
  and schedules a debounced write of `settings.json`.
- `MainWindowShellTests.ToggleThemeCommand_SwitchesTheVariantForTheWholeApplication` builds its own
  container over the real configuration directory; a toggle that now writes settings must run over
  `TestServices`' throwaway directory instead.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What is stored | `Theme = Dark` or `Theme = Light` — the variant the button switched to | What the reader sees is what they chose; the next start shows the same thing | Storing nothing and reading the variant back at exit (lost on a crash) |
| When the preference is `System` | The button switches to the opposite of the variant on screen and stores that, leaving "follow the system" | A click is an explicit choice; the settings page still offers "Follow the system" to go back | Cycling System → Dark → Light (a third click state nobody asked for) |
| Who applies the variant | The button sets it at once and stores it; the `Changed` handler applying the same variant again is a no-op | The switch must not depend on the App's subscription being wired, which it is not in a headless test | Relying on the `Changed` handler alone |
| Persistence timing | The service's normal debounced write | Same path as every other preference; `App` flushes on exit through the host's shutdown | An immediate flush per click |

## Steps

1. `MainWindowViewModel` takes `ISettingsService`; `OnToggleTheme` computes the next variant from
   `ActualThemeVariant`, sets `RequestedThemeVariant`, and calls
   `settings.Update(current => current with { Theme = next })`.
2. Doc comments on the command say it records the choice.
3. Tests (`MainWindowShellTests` over `TestServices`): the toggle switches the variant and stores
   `Light`, then `Dark`; after a flush the settings file carries the stored theme; the settings page
   ViewModel reports the new theme.

## Acceptance criteria

- Toggling the theme updates the "Theme" preference to the variant now shown, and the settings file
  carries it.
- A restart (a fresh settings load of that file) starts on that theme.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- A theme toggle on the start window.
