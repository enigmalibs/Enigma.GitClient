# FEATURE-F873 — Tool dialogs on the window background

**Item:** FEATURE-F873 — Tool dialogs on the window background
**Branch:** `feature/feature-f873-tool-dialog-background`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

The branches, tags and remotes dialogs are now drawn on the window's own background colour instead
of the library's lighter dialog surface, in both themes, so their lighter strips — the page headers,
the manual-merge band, the group headings — stand out from the card as they do in the window.

The library's `ContentDialog` template paints its card with `{DynamicResource EnigmaSurfaceHighBrush}`
on an unnamed `Border`, not with the control's `Background`, so the tool-dialog host now carries its
own `EnigmaSurfaceHighBrush`: a `SolidColorBrush` whose colour is `{DynamicResource
EnigmaBackgroundColor}`. The override lives in that host's resources only, so the questions shown on
`HostDialog` — delete this branch?, rename it to what? — keep the library's look, and the brush
follows a theme switch while the dialog is open.

## Files / modules touched

**Modified — App**

- `Views/MainWindow.axaml` — the `ToolDialog` host's `ContentDialog.Resources`, and the comment saying
  why the resource rather than `Background`

**Tests**

- `App.UnitTests/ToolDialogTests.cs` — the tool dialog's card is the window background's colour in the
  dark theme and, after a switch while it is open, in the light one (and not the library's surface);
  a question on the operations host keeps the library's surface. The first test was checked to fail
  with the override disabled.

## Deviations & follow-ups

- None from the plan.
- The card's border (`EnigmaBorderBrush`), shadow and the scrim are unchanged, which is what keeps a
  card of the window's own colour legible as a dialog.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2105 passed, 0 failed (2 new).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the test finds the card | The one `Border` of the dialog's template with a shadow | The card is unnamed in the library's template; the shadow is what makes it the card |
| Which tool the theme test opens | Tags | The lightest page to build; all three share the host, which is what is under test |
