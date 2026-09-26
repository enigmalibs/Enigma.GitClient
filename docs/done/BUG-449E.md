# BUG-449E — Theme toggle is forgotten on restart

**Item:** BUG-449E — Theme toggle is forgotten on restart
**Branch:** `bugfix/bug-449e-theme-toggle-persist`
**Run:** feature/2026-09-24-toolbar-refresh-theme-menus

## Summary

The repository toolbar's "Switch between the dark and light themes" button now records the theme it
switched to as the **Theme** preference (`Dark` or `Light`), through the settings service every other
preference goes through. The settings file carries it after the usual debounced write, so the next
start opens on the theme the reader left, and the settings page shows the choice at once. From
"Follow the system", a click switches to the opposite of what is on screen and records that; the
settings page is where the system is followed again.

## Files / modules touched

**Modified — App**

- `ViewModels/MainWindowViewModel.cs` — takes `ISettingsService`; `OnToggleTheme` (no longer static)
  puts the next variant on the application and calls `Update(current => current with { Theme = … })`

**Tests**

- `App.UnitTests/MainWindowShellTests.cs` — the variant test now runs over `TestServices` (the switch
  writes a preference, and must never write the developer's own); the switch records `Light` then
  `Dark`, and the settings page reads `Light`; after a flush, a fresh settings service over the same
  directory — a restart — reads `Light`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Who puts the variant on the application | The command, before recording it; the startup handler applying the same variant again is a no-op | The plan's decision; it keeps the switch working where the application's handler is not wired (headless tests) |
| When the file is written | The service's normal debounce; the host flushes on exit | Same path as every preference; no special case |

## Deviations & follow-ups

- None from the plan.
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Documentation sweep

- Nothing to change: README's "Preferences that stick: theme, …" is now true of the toolbar switch too.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1981 passed, 0 failed (2 new), first run.
