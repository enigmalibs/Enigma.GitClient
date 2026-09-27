# FEATURE-5689 — Tool dialogs on the secondary surface

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-5689-secondary-dialog-background`
**Run:** feature/2026-09-27-infobars-dialogs-diff-release

## Objective

The branches, tags and remotes dialogs are drawn on the window's background rather than on the
library's lighter dialog surface (FEATURE-F873) — through a resource override, because
Enigma.Avalonia.Desktop 1.0.0 gave no other lever. Version 1.1.0 ships that look as a feature of its
own: `Classes="secondary"` on a `ContentDialog` paints its card with the new
`EnigmaDialogSecondaryBackgroundBrush`. Use it, and drop the override.

## Context & constraints

- The three pages are shown by `ToolDialogService` on the `ToolDialog` host in
  `Views/MainWindow.axaml`, which today redefines `EnigmaSurfaceHighBrush` in its own `Resources` as
  a brush over `EnigmaBackgroundColor`. The confirmations are on `HostDialog` and keep the library's
  default surface.
- 1.1.0's `ContentDialog` theme paints the card (`PART_Card`) with `ContentDialog.Background`, set by
  the theme to `EnigmaSurfaceHighBrush`, and `^.secondary` sets it to
  `EnigmaDialogSecondaryBackgroundBrush` — `EnigmaDialogSecondaryBackgroundColor`, Dark `#1E1F22`,
  Light `#F7F8FA` (the window-background tone, so the look does not change).
- `ContentDialogService`/`ToolDialogService` do not reset a host's classes or `Background`; the look
  is a host setting.
- The package bump to 1.1.0 lands in FEATURE-A5D3-PHASE01, merged before this dev.
- `App.UnitTests/ToolDialogTests.cs` asserts the card colour in both themes, finding the card as the
  one template border with a shadow.

## Decisions

| Decision | Rationale |
|---|---|
| `Classes="secondary"` on the `ToolDialog` host; the `ContentDialog.Resources` override removed | The library's own lever, which the draft asks for; the override was a workaround for its absence |
| The XAML comment rewritten to say why the secondary class (content laid out for the window background) | The old comment explains a workaround that no longer exists |
| The tests assert `EnigmaDialogSecondaryBackgroundColor`, and find the card by its template part name `PART_Card` | The key is now the contract; 1.1.0 names the part, which is sturdier than "the border with a shadow" |

## Steps

1. `Views/MainWindow.axaml` — `ToolDialog` gets `Classes="secondary"`; remove its `Resources`
   override; update the comment above it.
2. `App.UnitTests/ToolDialogTests.cs` — the tool dialog's card is `EnigmaDialogSecondaryBackgroundColor`
   in the dark theme and, after a switch while open, in the light one; the host carries the
   `secondary` class and no local `EnigmaSurfaceHighBrush`; the confirmation host's card is still
   `EnigmaSurfaceHighColor`. Find the card by `PART_Card`.

## Acceptance criteria

- The branches, tags and remotes dialogs are drawn on `EnigmaDialogSecondaryBackgroundBrush` in both
  themes, and follow a theme switch while open.
- No resource override remains on the tool-dialog host.
- The confirmation dialogs are unchanged.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Any other dialog (the confirmations, the About box, the clone and create forms).
- Restyling the pages themselves.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which lever | `Classes="secondary"` | The draft asks for the new library values; it is the documented 1.1.0 feature | `Background="{DynamicResource EnigmaDialogSecondaryBackgroundBrush}"` on the host (works, but bypasses the library's named look); keeping the override |
| Which dialogs | The tool-dialog host only | It is the one the app changed; the draft names branches, tags and remotes | Every `ContentDialog` |
| How the test finds the card | `PART_Card` | 1.1.0 exposes the part by name | The shadow heuristic |
