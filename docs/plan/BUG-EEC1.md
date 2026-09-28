# BUG-EEC1 — Start page empty state not centred

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-eec1-start-empty-state`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Objective

On the start window's Repositories page, with no recent repository, the "No repositories yet" empty
state — icon above, headline, explanation below — sits against the left edge. It must be centred on
the page, horizontally and vertically, as every other page's empty state is.

## Context & constraints

- `RepositoriesPageView.axaml` is a `DockPanel`: the header docked `Top`, then the `EmptyState`, then
  the `ScrollViewer` of recent repositories. Only the **last** child fills; every other undocked child
  defaults to `Dock="Left"` and gets its desired width. The empty state is the middle child, so it is
  docked left at the width of its text, and the invisible scroll viewer takes the rest.
- `ChangedFilesPanelView.axaml` already records the same trap and its fix: the alternatives share one
  `Grid` cell instead of docking beside one another.
- `EmptyState`'s theme (`Themes/Controls.axaml`) stretches the control and centres its content, so a
  full-size cell is all it needs.

## Steps

1. `RepositoriesPageView.axaml`: put the `EmptyState` and the `ScrollViewer` in one `Grid` as the
   `DockPanel`'s last child.
2. Check the other `EmptyState` usages for the same shape (a non-last child of a `DockPanel`) and fix
   any found the same way.
3. Headless test: the Repositories page with no recent repository, in a window of the start window's
   size — the empty state spans the page's width below the header, and its content's centre is the
   page's centre.

## Acceptance criteria

- With no recent repository, the empty state is centred horizontally and vertically in the area under
  the header, at any window size.
- The list of recent repositories is unchanged when there are some.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The empty state's wording, icon or styling.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to centre it | One `Grid` cell for the empty state and the list, as `ChangedFilesPanelView` does | The root cause is the `DockPanel` docking a non-last child left; a shared cell is the house fix | `DockPanel.Dock="Top"` with a fixed height; `HorizontalAlignment` tweaks (the control already stretches) |
