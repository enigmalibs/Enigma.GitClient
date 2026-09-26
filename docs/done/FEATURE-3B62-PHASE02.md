# FEATURE-3B62-PHASE02 — Selectable remote rows

**Item:** FEATURE-3B62 — Selectable rows and branch drops
**Branch:** `feature/feature-3b62-phase02-selectable-remotes`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

The remotes list is a `ListBox`, so a remote can be selected by pointer or by keyboard, and the
selection survives the reads the page performs constantly.

The same shape as the branches list one phase earlier: the theme's selection and hover replace the
row's hand-rolled `:pointerover` background, the row template is otherwise untouched — its menu, its
three buttons, its pills and its separator line are all as they were — and `SelectedRemote` is
restored by name after every read, because `RefreshAsync` rebuilds every row on a refresh, on each
repository state change and after each operation.

The name is captured *before* the list is emptied: clearing it tells the `ListBox` its selection is
gone, and the two-way binding tells the page so.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/RemotesPageViewModel.cs` — `SelectedRemote`, captured by name before
  `RefreshAsync` clears the list and restored after it has refilled it
- `Views/Pages/RemotesPageView.axaml` — the `ScrollViewer` + `ItemsControl` become the `RemoteList`
  `ListBox` with a two-way `SelectedItem`; the item style gives the row the whole container; the
  row's own pointer-over style is gone

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/RemotesAndSyncTests.cs` — a "selection" section: the
  selection follows the name across a refresh, removing the selected remote clears it, and the
  rendered page's `ListBox` selection agrees with the page

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3B62.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Restoring the selection | By name, inside `RefreshAsync` | Every row is a new object after a read, and reads happen on every repository state change |
| Where the name is captured | Before the list is cleared | Clearing it nulls the page's own property through the two-way binding, so reading it afterwards reads nothing |
| The row's hover | The list's | One hover per row, and the list's is the one that agrees with its selection |
| The row template | Unchanged | Selection is how a row is picked, not what it contains; the menu and the buttons stay where the reader left them |

## Deviations & follow-ups

- **None from the plan.** All four acceptance criteria are covered.
- Nothing acts on the selected remote yet — like the branches, it is a selection the page offers, and
  the operations still run from the row. A details pane or a toolbar driven by it would be a feature
  of its own.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1722  failed: 0  succeeded: 1722  skipped: 0
```

Three tests added. No fix cycle was needed beyond re-indenting the XAML the template moved into.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both describe what the remotes page does — fetch, edit,
remove, add — and selecting a row adds no capability to state. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
