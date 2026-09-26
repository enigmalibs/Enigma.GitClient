# FEATURE-0DB4 — Bigger icons, readable selected rows

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-20-ui-polish-diff-page

## Objective

Two things the whole application does, done once and in one place: icons big enough to aim at — in
the toolbars, and on the rows of the branches, remotes and history lists — and text that stays
readable when the row it sits on is selected.

## Context & constraints

- Every icon in the application states its own size (`<ei:Icon Size="14" />`): 77 at `12`, 46 at
  `11`, 41 at `14`, 18 at `15`, 12 at `16`, 9 at `13`. There is no scale, so "a bit bigger" today
  means editing a hundred magic numbers and inventing the relationship between them again on the
  next view.
- `Enigma.Icons.Avalonia`'s `Icon.Size` is a `StyledProperty<double>`, so `<Style Selector="ei|Icon.toolbar">`
  can set it — which is how the rest of this application already expresses a role (`Button.toolbar`,
  `ToggleButton.segment`, `TextBlock.columnheader`).
- **Avalonia's value precedence puts a local value above every style setter.** A `TextBlock` that
  writes `Foreground="{DynamicResource EnigmaForegroundTertiaryBrush}"` in markup cannot be
  restyled by a `:selected` rule, whatever its classes say. `ChangedFilesPanelView.axaml` and
  `IntegrationsPageView.axaml` already carry `ListBoxItem:selected TextBlock.dim` styles that have
  never had any effect for exactly this reason — the same rows also set `Foreground` locally. The
  local values have to go for the class to mean anything.
- The selection brush is FluentTheme's own: `Enigma.Avalonia.Desktop` restyles `TextBox` and
  `ComboBox` only, so a `ListBoxItem`'s selected background is the framework's. What this item
  changes is the text drawn on it, not the plate underneath.
- `Themes/Styles.axaml` is merged after `FluentTheme` in `Application.Styles` and is where house
  classes live; `Themes/Controls.axaml` holds the `ControlTheme`s of our own controls.
- Rows inside a `ContextMenu` or a `ControlTemplate` (the `RefBadge` icon, `EmptyState`'s 44 px
  glyph) are not "toolbar" or "row" icons and keep their own sizes.
- **Baseline:** on the branch this run started from, `dotnet build` is clean and the whole suite is
  green (1728 tests). That is the gate for every phase.

## PHASE01 — One icon scale for the whole app

**Branch:** `feature/feature-0db4-phase01-icon-scale`
**Status:** DONE — see `docs/done/FEATURE-0DB4-PHASE01.md`

### Steps

1. `Themes/Styles.axaml`: an icon scale, as classes on `ei|Icon` — `header` (20, the icon beside a
   page title), `toolbar` (18, an icon-only toolbar button or segment toggle), `row` (16, the
   leading icon and the action buttons of a list row), `pill` (12, an icon inside a badge or a
   counter). One comment stating what each role is, so the next view picks a class rather than a
   number.
2. Same file: `Button.toolbar` and `ToggleButton.segment` padding re-balanced for the larger glyph
   (the plate must still read as a square around the icon, not as a bar).
3. Replace the hard-coded `Size` of every icon that has one of those roles with the class, across
   `Views/MainWindow.axaml`, `Views/Pages/*.axaml` and `Views/Panels/*.axaml` — toolbars and page
   headers first, then the row icons and row action buttons of the branches, tags, remotes,
   repositories, integrations, changed-files and stash lists.
4. Leave icons that are part of a control template, a badge or an empty state as they are, and say
   so in the completion doc.
5. Tests — `tests/.../ShellRenderTests.cs`: the four size classes resolve and a realised toolbar
   button's icon is drawn at the toolbar size, a realised row icon at the row size; every page still
   builds and lays out.

### Acceptance criteria

- The toolbar icons of the diff viewer, the history, branches, remotes and changes pages, and the
  repository strip are visibly larger than a row's text, and every one of them takes its size from a
  class rather than a number in markup.
- A list row's leading icon and its action buttons are larger than they were, and the same size on
  every page.
- No icon changes size by accident: badges, the empty state and control templates keep theirs.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — Readable text on a selected row

**Branch:** `feature/feature-0db4-phase02-selected-row-text`
**Status:** DONE — see `docs/done/FEATURE-0DB4-PHASE02.md`

### Steps

1. `Themes/Styles.axaml`: the two dimmed text roles as classes — `TextBlock.dim` (secondary) and
   `TextBlock.faint` (tertiary) — and the same two on `ei|Icon`, each with a base setter naming the
   brush it has today.
2. Same file: `ListBoxItem:selected`, `TreeViewItem:selected` and `ListBoxItem:selected Border.pill`
   raise those roles to `EnigmaForegroundBrush`, so the row being read is the row that reads.
3. Remove the local `Foreground` from every row-template `TextBlock` and `ei:Icon` that carries one
   of the two dimmed brushes, replacing it with the class — `Views/Pages/HistoryPageView.axaml`,
   `BranchesPageView.axaml`, `RemotesPageView.axaml`, `RepositoriesPageView.axaml`,
   `IntegrationsPageView.axaml`, `ChangesPageView.axaml` (the stash rows) and
   `Views/Panels/ChangedFilesPanelView.axaml`.
4. Delete the per-view `:selected TextBlock.dim` styles from `ChangedFilesPanelView.axaml` and
   `IntegrationsPageView.axaml`, now that the house style says it once and it works.
5. Page headers, toolbars and settings text keep their local brushes: they are never drawn on a
   selection.
6. Tests — `tests/.../ShellRenderTests.cs`: a realised row of a selected `ListBoxItem` paints its
   dimmed text in the foreground brush and an unselected one in the dimmed brush; the same for a
   selected `TreeViewItem` in the changed-files tree.

### Acceptance criteria

- Selecting a row in the history, branches, remotes, repositories, integrations or changed-files
  lists leaves every word on it readable, including the author, the date, the short hash, the
  upstream and the counters.
- Deselecting puts the dimmed colours back.
- The dimmed colours are unchanged everywhere else.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- Making the icon scale a user preference, or reading it from the settings page.
- Changing the selection brush itself, or the row heights.
- Restyling `TextBox` / `ComboBox` (the control library owns those).
- The conflict resolution page's panes, which draw their own colours for a different reason.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How a size is expressed | A class per role on `ei\|Icon` | One place to tune, and the next view picks a role rather than inventing a number | `{DynamicResource IconSizeToolbar}` per icon (still a value per icon, and invisible in the markup); editing the numbers in place (no scale at all) |
| How much bigger | Toolbar 14→18, row 14→16, header 18→20 | "A bit bigger", about a quarter, which is the step that changes a hit target without changing a layout | Doubling (rows would have to grow with it) |
| The selected-row fix | Brighten the text, keep the theme's selection plate | The plate is the framework's contract and is consistent with every other list; the contrast problem is the grey text on it | Painting our own selection brush (fights the theme, and returns on the next Avalonia update) |
| How the brightening is expressed | Classes plus `:selected` styles, local `Foreground`s removed | A local value outranks every style in Avalonia — the existing styles prove it, having never once fired | A converter per row bound to `IsSelected` (a binding per TextBlock, and a second source of truth about selection) |
| Class names | `dim` (secondary) and `faint` (tertiary) | `dim` is already the house name in two views; `faint` reads as one step further down | `secondary` / `tertiary` (renames what is already there for no gain) |
| Scope of the class migration | Row templates of selectable lists | They are the only text ever drawn on a selection; a page header cannot be selected | Migrating every dimmed TextBlock in the app (churn with no behaviour change) |
