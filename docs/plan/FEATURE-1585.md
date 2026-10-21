# FEATURE-1585 — Info bars at the bottom left

**Status:** DONE — see `docs/done/FEATURE-1585.md`
**Type:** FEATURE
**Branch:** `feature/feature-1585-infobar-bottom-left`
**Run:** feature/2026-10-21-ui-polish-release-650

## Objective

The info bar is no longer a band across the top of the window. It sits in the bottom-left corner,
about a quarter of the window wide, where it hides nothing important.

## Context & constraints

- Both windows (`StartWindow`, `MainWindow`) host one `InfoBar` (`HostInfoBar`). It is the last child
  of the root `Panel`, and `MainWindowShellTests` asserts that it is.
- The control comes from Enigma.Avalonia.Desktop 1.2.0. Its theme sets `HorizontalAlignment=Stretch`
  and `VerticalAlignment=Top`. Its template wraps the card in a 24 px margin.
- The left of each window is a navigation rail (`RepositoryRailWidth` 72, `StartRailWidth` 88). Its
  footer, *Profiles* and *Settings*, sits in the bottom-left corner.
- `MainWindow` already sizes its tool dialog from the window in code-behind (`ToolDialogSizeFor`). That
  is the precedent for anything sized relative to the window.

## Steps

1. A small static helper in `Views` (`InfoBarPlacement`):
   - the bar's width for a window width and a rail width: 25 % of the window, at least 360, never
     more than the room right of the rail;
   - a `Place` method that sets the bar's `Width`, and a left margin equal to the rail width.
2. Both windows' XAML: `HostInfoBar` gets `HorizontalAlignment="Left"` and
   `VerticalAlignment="Bottom"`, and stays the last child of the root panel.
3. Both code-behinds place the bar at construction and on every `SizeChanged`. The rail width is
   read from the window's navigation view.
4. Tests (Desktop suite, compiled):
   - the helper's width at several window widths (25 %, the minimum, the cap);
   - both windows' bar is left and bottom aligned, and clear of the rail.

## Acceptance criteria

- On both windows the info bar appears in the bottom-left corner, right of the navigation rail.
- Its width is a quarter of the window, never less than 360 px, and never wider than the room right
  of the rail.
- It follows a window resize.
- It is still the last child of the root panel, so it overlays the dialogs as before.
- The build is clean with zero warnings. The Core suites are green. The Desktop suite compiles.

## Out of scope

- Restyling the bar's card (colours, icon, close button): that is the library's.
- Stacking several messages at once.
- Changing the bar in Enigma.Avalonia.Desktop itself.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Corner | Bottom-left, starting right of the navigation rail | Where you asked; the rail's footer (*Profiles*, *Settings*) is the one important thing in that corner | The window's very corner, over the rail |
| Width | 25 % of the window width | The middle of your 20–30 % | A fixed width |
| Minimum width | 360 px | 25 % of a minimum-size window (760/900) leaves a card too narrow to read | No minimum |
| How the width follows the window | Code-behind on `SizeChanged`, through a testable static helper | The tool dialog's precedent; a star-column wrapper would stop the bar being the root panel's last child | A wrapping `Grid` with star columns; a converter |
| Where the change lives | The app's two windows | The control's alignment is a theme default the instance can override | A library change |
