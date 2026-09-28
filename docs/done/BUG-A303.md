# BUG-A303 — File line menus only open on the text

**Item:** BUG-A303 — File line menus only open on the text
**Branch:** `bugfix/bug-a303-whole-line-file-menus`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

A file line's context menu opened only over the line's row. That affected:

- the Changes page's *Not staged* and *Staged* lists;
- the History diff view's file list.

All three are `ChangedFilesPanelView`.

The menu belongs to the row template's `Grid`, which FEATURE-5860 painted so that the gaps between
its columns hit. That grid is only the container's **content**, though. It sits inside the list item's
padding, and inside the tree item's indentation and chevron. A right-click there hit the container,
which carries no menu, so nothing opened. FEATURE-5860's test only right-clicked between two columns,
inside the grid, and missed it.

The panel now handles `ContextRequested` itself. A request that no row has already answered, raised
inside a `ListBoxItem` or `TreeViewItem` whose data is a file line, opens that line's own row menu at
the pointer. The nearest container wins, so in a tree a nested file's indentation opens the file's
menu, not its folder's. There is still one menu definition, with the same items and commands.

## Files / modules touched

**Created**

- `docs/done/BUG-A303.md`

**Modified**

- `src/Enigma.GitClient.App/Views/Panels/ChangedFilesPanelView.axaml.cs` — the bubbling
  `ContextRequested` handler.
- `tests/Enigma.GitClient.App.UnitTests/WholeLineMenuTests.cs` — 4 tests:
  - the panel in list and tree mode;
  - the Changes page's two lists;
  - the History diff view.

  Each makes a real headless right-click in the container's corner, at the start and the end of the
  line, between its columns and over its name, and checks that the line's own menu opens each time.
- `docs/roadmap.md`, `docs/plan/BUG-A303.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Diagnosis first | The new tests were run before the fix: all four failed with "a right-click in the container's corner did not open the line's menu" | It proves the container's area was the gap, in both modes and on both pages |
| Handled requests | Not listened to (bubbling, `handledEventsToo: false`) | The row's own menu handles its request, so only a right-click outside every row reaches the panel, and nothing opens twice |
| Opening point | `ContextMenu.Open(row)` | The menu's default placement is the pointer, as for a right-click on the row itself |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing user-facing describes where a menu opens. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors. A missing `using Avalonia.Input` and a
  missing `using Enigma.GitClient.Core.Diff` in the test were fixed before the first run.
- `dotnet test --solution Enigma.GitClient.slnx`: **2458 passed**, 0 failed, 0 skipped (4 new, 2 of
  them as theory rows).
- Fix budget: no fix cycle.
