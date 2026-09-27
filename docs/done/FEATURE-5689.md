# FEATURE-5689 — Tool dialogs on the secondary surface

**Item:** FEATURE-5689 — Tool dialogs on the secondary surface
**Branch:** `feature/feature-5689-secondary-dialog-background`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Summary

The branches, tags and remotes dialogs are drawn on Enigma.Avalonia.Desktop 1.1.0's **secondary
dialog surface**. The `ToolDialog` host in `Views/MainWindow.axaml` carries `Classes="secondary"`,
which paints its card with `EnigmaDialogSecondaryBackgroundBrush`. The workaround from FEATURE-F873
is gone: before 1.1.0 there was no lever but a redefined `EnigmaSurfaceHighBrush` in the host's own
`Resources`.

The look is unchanged. The secondary colour is the window-background tone, Dark `#1E1F22` and Light
`#F7F8FA`, which is what the override produced. The confirmations on `HostDialog` keep the library's
default surface.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.App/Views/MainWindow.axaml` — `Classes="secondary"` on `ToolDialog`, its
  `Resources` override removed, the comment rewritten
- `tests/Enigma.GitClient.App.UnitTests/ToolDialogTests.cs`:
  - `TheToolDialog_IsDrawnOnTheSecondaryDialogSurface_InBothThemes` (renamed): the host has the
    class and no local brush, and its card is `EnigmaDialogSecondaryBackgroundColor` in both themes,
    switched while open.
  - The confirmation test also asserts its host has no `secondary` class.
  - The card is found by its 1.1.0 template part `PART_Card`.
- `docs/roadmap.md`, `docs/plan/FEATURE-5689.md` — statuses

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| What makes the test prove the new lever | Assert the class and the absence of the local brush, alongside the colour | The secondary colour equals the old override's colour, so a colour check alone would pass with the workaround still in place |
| How to find the card | `PART_Card` whose `TemplatedParent` is the dialog | 1.1.0 names the part, and the templated-parent check keeps a page's own borders out |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn. The touched files are LF.

## Documentation sweep

`RELEASENOTES.md` 1.0.0 says the tool dialogs sit on "the window's own background". That is still
true, because the secondary surface is that tone. Nothing else mentions the dialog surface, so
nothing was edited.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors, and no `AVLN` XAML warnings in the App
  build.
- `dotnet test --solution Enigma.GitClient.slnx`: **2227 passed**, 0 failed (the two dialog-surface
  tests updated), with no fix cycle.
