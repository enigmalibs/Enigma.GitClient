# FEATURE-68B9 — A clear button in the home filter

**Status:** DONE — see `docs/done/FEATURE-68B9.md`
**Type:** FEATURE
**Branch:** `feature/feature-68b9-home-filter-clear`
**Run:** feature/2026-10-14-home-filter-clear-release

## Objective

The start window's filter box (`RepositoriesPageView`, FEATURE-8970 PHASE01) gets a **clear button at
its right edge**: an `×` inside the box, shown while the box holds text, that empties it and brings
every repository back — what Escape already does, now with the mouse.

## Context & constraints

- **The box:** `TextBox Name="RepositoryFilter"` in `Views/Pages/RepositoriesPageView.axaml`, bound to
  `SearchText`, placeholder *Filter by name or path*, automation name *Filter repositories*, Escape
  bound to `ClearSearchCommand`. It sits in the `DockPanel` shown while `HasRepositories`, so it stays
  on screen in the *No repository matches this filter* state too.
- **The command:** `RepositoriesPageViewModel.ClearSearchCommand` already exists — it sets
  `SearchText` to empty, and can run while `SearchText.Length > 0` (spaces included).
- **The theme:** Enigma.Avalonia.Desktop restyles `TextBox` through FluentTheme's brush keys only, so
  Fluent's template — and its `InnerRightContent` slot — is in place.
- **House look:** small `X` buttons on the history's chips are `Button.toolbar` with an
  `ei:Icon Kind="X" Classes="pill"` (12 px). `Button.toolbar` draws a one-pixel frame, which inside a
  text box would read as a box in the box.
- **Tests:** `RepositoryFilterTests` (Desktop, headless). Per the project's standing instruction the
  Desktop suite is written and compiled but **not run** on this Windows machine; the Core suites are
  run.

## Steps

1. `RepositoriesPageView.axaml`: give `RepositoryFilter` an `InnerRightContent` holding a `Button`:
   - `Name="ClearFilter"`, `Classes="toolbar"`, frame removed (`BorderThickness="0"`), a small
     padding and right margin so it sits inside the box's border;
   - `Command="{Binding ClearSearchCommand}"`;
   - `IsVisible` bound to `SearchText` through `StringConverters.IsNotNullOrEmpty` — the command's own
     rule;
   - `Focusable="False"`, so a click leaves the caret in the box (the keyboard has Escape);
   - `ToolTip.Tip="Clear the filter (Escape)"`, `AutomationProperties.Name="Clear the filter"`;
   - content `<ei:Icon Kind="X" Classes="pill" />`.
2. Tests in `RepositoryFilterTests` (compiled):
   - the button is hidden while the box is empty, and shown once there is text (spaces included);
   - clicking it empties the box, shows every repository in the stored order, and hides it again;
   - it sits inside the box, at its right edge;
   - it works from the *No repository matches* state.

## Acceptance criteria

- With text in the filter box, an `×` button shows inside the box at its right edge; with none, it is
  not there.
- Clicking it empties the box and every repository shows again, in the stored order; the button then
  disappears.
- Escape still clears the box. The tab order is unchanged.
- The button has an automation name and a tooltip.
- Build clean (zero warnings, zero AVLN warnings); Core suites green; Desktop tests compile.

## Out of scope

- The other filter boxes (Tags, Branches, the changed files, the history's search).
- Any change to the filter rule, the reordering, or the ViewModel's API.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the button goes | Inside the box, at its right edge (`InnerRightContent`) | What "in the right of the search box" describes, and the usual place for a filter's clear button; the box keeps its full width | A toolbar button beside the box (the history's search); Fluent's `clearButton` class |
| When it shows | Whenever the box holds text, spaces included | The same rule as `ClearSearchCommand`'s can-execute: anything typed can be cleared | Fluent's `clearButton` (only while the box has focus — gone as soon as the user clicks elsewhere); always shown but disabled |
| What it runs | `ClearSearchCommand` | One way to clear, shared with Escape; the ViewModel stays the source of truth | `TextBox.Clear()` in the view |
| Focus | The button is not focusable | A click keeps the caret in the box to type again; the keyboard already has Escape, and the tab order does not grow a stop that disappears | A focusable button that takes the focus away and then vanishes |
| Look | The toolbar button without its frame, the chips' 12 px `X` | Matches the app's other small clear buttons; no new style for one control | The framed toolbar button; a new theme style |
| Visibility binding | `StringConverters.IsNotNullOrEmpty` on `SearchText` | Avalonia's own converter (the app already uses `BoolConverters.And`); no ViewModel change | A new `HasSearchText` property |
| Other filter boxes | Left as they are | Scope is the home page; noted as a follow-up | All five at once |
| Breakdown | One item, one dev | A single reviewable view change | — |
