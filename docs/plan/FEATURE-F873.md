# FEATURE-F873 — Tool dialogs on the window background

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-f873-tool-dialog-background`
**Run:** feature/2026-09-26-release-1-0-0

## Objective

The branches, tags and remotes dialogs are drawn on the library's light dialog surface, which does
not go with their content: those pages were designed for the window's own background, with lighter
strips (their headers, the manual-merge band, the group headings) standing out from it. Draw the
card of these three dialogs on the window's background instead, in both themes.

## Context & constraints

- The three pages are shown by `ToolDialogService` on the `ToolDialog` host placed in
  `Views/MainWindow.axaml`, a host of their own under `HostDialog` (the confirmations).
- The library's `ContentDialog` template paints its card with a hard-coded
  `{DynamicResource EnigmaSurfaceHighBrush}` on an unnamed `Border` (read from the decompiled
  `Themes/Controls/ContentDialog.axaml` of Enigma.Avalonia.Desktop 1.0.0) — not through the control's
  own `Background` property, so setting `Background` on the host does nothing.
- A `DynamicResource` inside a template resolves through the templated parent, so a resource the host
  itself carries wins over the application's for that host alone.
- Inside the pages, `EnigmaSurfaceHighBrush` is used only by the library's own editors on hover,
  which the three pages do not use; `ProgressOverlayCard` uses it, but lives on the `Overlay` host.
- Brushes must follow a theme switch: the library's brushes are `SolidColorBrush`es whose `Color` is a
  `DynamicResource` to a theme-scoped colour key; the window background colour is
  `EnigmaBackgroundColor`.

## Decisions

| Decision | Rationale |
|---|---|
| Override `EnigmaSurfaceHighBrush` in the `ToolDialog` host's own `Resources` with a `SolidColorBrush` whose `Color` is `{DynamicResource EnigmaBackgroundColor}` | The only lever the template leaves; scoped to the host, so the confirmations on `HostDialog` keep the library's look |
| The window's own colour, not something darker still | "More like the window background" is the first thing the draft asks; the card is still set apart by its border, its shadow and the scrim behind it |

## Steps

1. `Views/MainWindow.axaml` — give `ToolDialog` a `ContentDialog.Resources` entry redefining
   `EnigmaSurfaceHighBrush` as above, with a comment saying why the resource and not `Background`.
2. Tests (`App.UnitTests/ToolDialogTests.cs`): with a tool dialog open in the realised window, the
   card's background is the window background's colour — in the dark theme and, after a switch, in
   the light one; the confirmation host's card still resolves the library's surface colour.

## Acceptance criteria

- The branches, tags and remotes dialogs are drawn on the window background colour, in both themes,
  and follow a theme switch while open.
- The confirmation dialogs are unchanged.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Restyling the pages themselves, or any other dialog.
- Changing the library's theme.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to change a card whose brush is hard-coded in the library's template | A scoped resource override on the tool-dialog host | Works with the template as shipped, touches one host, follows the theme | A style on `/template/ Border` (the card is unnamed, and the template's own binding may outrank a style); forking the control theme (a copy of the library to keep in step) |
| Which colour | `EnigmaBackgroundColor`, the window's own | The draft's first choice; the pages' lighter strips read as they do in the window | A hand-picked darker hex (no theme reactivity, breaks the house brush rule); `EnigmaSurfaceLow` (still a light surface in the dark theme) |
| Which dialogs | The tool-dialog host only | The draft names branches, tags and remotes; confirmations are a different kind of dialog | Every `ContentDialog` in the app |
