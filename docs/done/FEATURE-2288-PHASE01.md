# FEATURE-2288-PHASE01 — The commit diff dialog

**Item:** FEATURE-2288 — Diffs in a dialog, not a panel (PHASE01)
**Branch:** `feature/feature-2288-phase01-diff-dialog`
**Run:** feature/2026-09-17-graph-space-diff-dialog

## Summary

The history page is the graph again. Selecting a line no longer opens a panel underneath it; it puts
what that commit changed in a dialog over the whole page.

The page's own `Workspace` is now a plain `Panel` holding the empty state and the commit list — the
splitter row, the detail row and the pixel arithmetic that opened and collapsed it (`BUG-6CE6`'s
`ApplyDetailsHeight`, `DefaultDetailsHeight`, `MinimumDetailsHeight`) are gone, because the list is
the page's body whatever is selected. What the panel used to hold — the commit's author, date and
hash, the `ChangedFilesPanelView`, the splitter and the `DiffViewerView` — moved unchanged into a
`ContentDialog` declared as the last child of the page's root panel, where its scrim covers the page.

The dialog is given its own instance rather than going through `IContentDialogService`: that service
drives the window's single host, whose six size properties are documented as *not* being reset
between dialogs, so a card sized for a diff would have leaked its size into every confirmation in the
application. Its size is a fraction of the page rather than a number: `DialogWidth`, `DialogHeight`
and both maxima bind to the page root's `Bounds` through `DialogSizing.Fill`, so the card is 94% of
the page and follows every window resize. The maxima have to be bound too — the control's own default
maximum is 600, and would otherwise clamp the card.

Whether it is on screen is the ViewModel's `IsDiffDialogOpen`, set by `SelectedRow`'s setter and
cleared by `CloseDiffDialogCommand`. The view follows that property with the control's `ShowAsync` /
`HideAsync`, and the control's `Closed` event writes `false` back — which is what makes the cross, the
bottom Close button, Escape and a click on the scrim all mean the same thing to the page. Closing
deliberately leaves the selection alone: it is the start point "create a branch/tag here" falls back
to, and the highlight that says where the reader is in the history. Losing the selection — what a
reload after a checkout does — closes the dialog with it.

## Files / modules touched

**Created — App**

- `Controls/DialogSizing.cs` — the fraction of a page a full-size dialog takes, and the converter
  that applies it to a dimension

**Modified — App**

- `ViewModels/Pages/HistoryPageViewModel.cs` — `IsDiffDialogOpen`, set from `SelectedRow`'s setter,
  and `CloseDiffDialogCommand`
- `Views/Pages/HistoryPageView.axaml` — the root is a `Panel`; `Workspace` is the graph alone; the
  dialog carries the commit header with the close cross, the changed files, the splitter and the diff
- `Views/Pages/HistoryPageView.axaml.cs` — the detail row's height machinery is replaced by
  `ApplyDialogState` and the dialog's `Closed` handler

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the four `DetailPanel_…` tests are
  replaced by six `DiffDialog_…` ones (closed until a commit is selected and the list keeping the
  page's full height; the dialog carrying the subject, the files and the diff, and following the
  selection; the card being 94% of the page and shrinking with the window; closing without losing the
  selection; a close from the control reaching the page; the selection going away closing it) plus a
  theory over `DialogSizing`

**Modified — docs**

- `RELEASENOTES.md` — the graph section now says what selecting a line does, and the double-click
  line names the selected line
- `docs/roadmap.md`, `docs/plan/FEATURE-2288.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the commit's subject is drawn | The dialog's own `Title` | The control already draws and styles a title, and binding it keeps the header row down to the metadata and the cross |
| Where the cross sits | The right of the content's first row, under the title | The control's title area draws no close glyph and templating it would mean forking the library's theme for one button; the rendered frame reads as a cross in the card's top right |
| How the page root is reached from the binding | A named `Panel` and `#PageRoot` | Element-name binding says exactly which bounds are meant, where `$parent[UserControl]` would have depended on where the dialog sits in the tree |
| Whether the double-click handler stays | It stays | It still fires on a row that is already selected, which is the gesture the release notes now describe |

## Deviations & follow-ups

- **None from the plan.** All seven acceptance criteria are covered.
- **Consequence worth stating.** A double-click on a row that is *not* selected now opens the dialog
  on its first click, so the second click lands on the dialog rather than on the row: checkout on
  double-click remains available on the selected row and in the row's context menu. The release notes
  say so. `PHASE02` adds the menu entry that reopens the dialog, which is the other half of this.
- **Observation.** The headless rendering tests that read the diff's text out of the page
  (`ChangedFilesPanelTests`, `DiffViewerTests`) passed untouched: what they look for is now drawn
  inside the dialog, which is part of the page's visual tree.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build
  0 Warning(s)
  0 Error(s)

dotnet test
  Test run summary: Passed!
  total: 1635  failed: 0  succeeded: 1635  skipped: 0
```

Seven tests are new (six over the dialog, one theory over the sizing converter) and four are gone with
the panel they covered. No fix cycle was needed. The rendered frame the diff test writes
(`snapshots/history-page-diff.png`) was read back to confirm the card, its title, the cross, the file
tree, the side-by-side diff and the bottom Close button.

## Documentation sweep

Scanned the README and the release notes. The README describes the changed files and the diff without
saying where they are drawn, which this dev leaves true. The release notes' graph section did not: it
now names the dialog, and its double-click line names the selected line, because the first click on an
unselected row opens the dialog.
