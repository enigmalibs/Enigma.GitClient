# FEATURE-2288 — Diffs in a dialog, not a panel

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-2288-phase01-diff-dialog`, `feature/feature-2288-phase02-row-menu`
**Run:** feature/2026-09-17-graph-space-diff-dialog

## Objective

Give the history page back to the graph. Show what a commit changed in a content dialog almost as
large as the window, which opens the moment a line is selected and closes from a cross at its top
right or a Close button at its bottom — instead of the bottom panel the page carries today.

## Context & constraints

- Asked for as: "Now it's a bottom panel below the history graph. I want that when a line in the
  graph is selected, instead of displaying it in a bottom panel, it shows a content dialog (almost
  as big as the Window and that resizes with the window) displaying the diffs and with a cross at
  the top right to close it (or/and a close button in the bottom). … only showing the history graph
  without a bottom panel, and as soon as the user selects a line, the content dialog is displayed."
- Today `HistoryPageView.axaml` ends in a three-row `Grid` named `Workspace`: the commit list, a
  `GridSplitter`, and a `CommitDetails` `Border` holding the selected commit's header, the
  `ChangedFilesPanelView` (320 wide), a column splitter and the `DiffViewerView`. The third row's
  height is pixel-valued and driven from `HistoryPageView.axaml.cs` — `ApplyDetailsHeight` opens it
  to a remembered height when `HasSelection` turns true and collapses it to 0 when it turns false.
  That machinery is `BUG-6CE6`'s fix, and it goes away with the panel it exists for.
- The ViewModel already does all the loading: `SelectedRow`'s setter publishes the commit to the
  shell, notifies the header properties and starts `LoadChangedFilesAsync`, whose panel selection
  drives `ShowSelectedFileAsync` into `Diff`. None of that changes — only where the result is drawn.
- `Enigma.Avalonia.Desktop`'s `ContentDialog` is the house modal: a `ContentControl` with a scrim, a
  `Title`, free `Content`, up to three buttons, `ShowAsync()`/`HideAsync()`, a `Closed` event, and
  six size properties (`DialogWidth`/`DialogHeight`, defaulting to `NaN` = auto, plus min and max —
  whose defaults, 600 wide, would clamp anything this size).
- The library's `IContentDialogService` drives **one** host, the window's, and its documented
  contract is that the six size properties are *not* reset between dialogs. A diff dialog sized to
  the window would therefore leak its size into every confirmation in the application, so this
  dialog gets its own `ContentDialog` instance inside the history page instead of going through the
  shared host.
- The control's `ShowAsync` hands out a completion source per call and `CloseDialog` resolves it, so
  the dialog is opened and closed through those two methods — never by writing `IsOpen` from a
  binding, which would re-enter a completion source that is already resolved.
- Double-click already checks a row out (`OnCommitDoubleTapped` → `Activate`), and it is a released
  behaviour. A modal that opens on the first click of a double-click eats the second one, so nothing
  in this item may open the dialog from a *tap*: the dialog opens on a selection **change**, which
  leaves double-click working exactly as before on the row that is already selected.
- Closing the dialog must not clear the selection: `RepositoryContext.SelectedCommit` is what
  `BranchOperations` and `TagOperations` fall back to as the start point for "create a branch/tag
  here", and the row's highlight is what tells the reader where they are in the history.
- `ChangedFilesPanelView` and `DiffViewerView` are moved as they are. Both bind their own
  `DataContext` (`Files`, `Diff`) and neither knows what contains it.

## PHASE01 — The commit diff dialog

**Status:** TODO

### Steps

1. `ViewModels/Pages/HistoryPageViewModel.cs`:
   - `IsDiffDialogOpen` — a settable observable `bool`, the page's own statement of whether the
     diffs are on screen.
   - `SelectedRow`'s setter sets it: `true` for a row, `false` for `null` (so the reload that
     follows a checkout puts the dialog away with the selection it invalidated).
   - `CloseDiffDialogCommand` — a `RelayCommand` that sets it to `false`, for the cross.
2. `Controls/DialogSizing.cs` — how large a dialog drawn over a page is: a `Fraction` constant
   (0.94) and a `FuncValueConverter<double, double> Fill` that scales a page dimension to the
   dialog's, floored at zero. One place to change, and the arithmetic a `{Binding}` cannot do.
3. `Views/Pages/HistoryPageView.axaml`:
   - The root becomes a `Panel` holding the existing `DockPanel` and, as its last child, the dialog
     — so the scrim covers the whole page.
   - `Workspace` loses its splitter row and its details row: the commit list is the page's body and
     keeps the full height whatever is selected.
   - `<contentDialog:ContentDialog x:Name="DiffDialog">` with `Title="{Binding SelectedSubject}"`,
     `CloseButtonText="Close"` (the Close button at the bottom the draft asks for), and its four
     size properties — `DialogWidth`/`DialogMaxWidth` and `DialogHeight`/`DialogMaxHeight` — bound
     to the page root's `Bounds` through `DialogSizing.Fill`, so the card is 94% of the page and
     follows every window resize. Both the explicit size and the maximum are bound, because the
     maximum's own default would otherwise clamp the card to 600.
   - Its `Content` is the panel the workspace used to hold: a header row carrying the author, the
     date, the hash and the body — with the cross (`ei:Icon Kind="X"`, `Classes="toolbar"`,
     `AutomationProperties.Name="Close"`) at its right end, bound to `CloseDiffDialogCommand` — over
     the `ChangedFilesPanelView` / `GridSplitter` / `DiffViewerView` grid, unchanged.
4. `Views/Pages/HistoryPageView.axaml.cs`: `DefaultDetailsHeight`, `MinimumDetailsHeight`,
   `DetailsHeight`, `_detailsRow` and `ApplyDetailsHeight` go; the `HistoryPageViewModel`
   subscription stays and now follows `IsDiffDialogOpen` — `ShowAsync()` when it turns true and the
   dialog is closed, `HideAsync()` when it turns false and the dialog is open, both guarded by
   `DiffDialog.IsOpen` so a completion source is never resolved twice. The dialog's `Closed` event
   writes `false` back to the page, which is what makes Escape, the scrim and the Close button agree
   with the cross. The double-click handler stays as it is.
5. Tests — `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`: the four `DetailPanel_…`
   tests are the bottom panel's regression suite and are replaced by the dialog's, over the same
   `ShowHistoryPageAsync` helper (now returning the dialog rather than the workspace's rows): the
   page shows only the graph until a row is selected; selecting one opens the dialog and gives the
   list the whole page; closing it leaves the selection and the shell's selected commit alone;
   clearing the selection closes it; the card is 94% of the page and follows a resize.
6. Tests — `tests/Enigma.GitClient.App.UnitTests/`: a `DialogSizing` case, and the two rendering
   tests that select a row and read the drawn text (`ChangedFilesPanelTests`,`DiffViewerTests`)
   confirmed still green now that what they read is drawn inside the dialog.

### Acceptance criteria

- With nothing selected the page is the graph and nothing else: no detail panel and no splitter in
  the visual tree, and the commit list's height is the workspace's height.
- Selecting a row opens the dialog, which carries the commit's subject, its author, date and hash,
  the changed files and the diff viewer.
- The card measures 94% of the page in both dimensions, and both change when the window is resized.
- The cross, the bottom Close button and Escape all close it; after closing, `SelectedRow` and
  `RepositoryContext.SelectedCommit` still hold the commit and the row is still highlighted.
- Setting the selection to `null` — what a reload after a checkout does — closes the dialog.
- Moving the selection to another row while the dialog is open keeps it open and shows the new
  commit.
- `dotnet build` clean with zero warnings; the whole suite green.

## PHASE02 — Reopen it from the row menu

**Status:** TODO

### Steps

1. `ViewModels/Pages/CommitRowViewModel.cs`: `HistoryRowCommands` gains `ShowChanges`, a
   `RelayCommand<CommitRowViewModel>` documented as what reopens the dialog for a row.
2. `ViewModels/Pages/HistoryPageViewModel.cs`: the handler selects the row when it is not the
   selected one — the setter then opens the dialog — and otherwise just sets `IsDiffDialogOpen` to
   `true`, which is the case the menu exists for.
3. `Views/Pages/HistoryPageView.axaml`: "Show what it changed" as the first item of a row's context
   menu, above "Create branch here…", with a separator after it.
4. Tests — `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs`: the command opens the dialog
   for the row that is already selected after it was closed, and selects and opens for a row that is
   not.

### Acceptance criteria

- After closing the dialog, "Show what it changed" on the selected row brings it back on the same
  commit, with no selection change.
- The same item on another row selects that row and opens the dialog on it.
- The item is offered on the uncommitted-changes row too, where it shows the working tree's diff.
- `dotnet build` clean with zero warnings; the whole suite green.

## Out of scope

- The changed-files panel and the diff viewer themselves: they move, they do not change.
- A window-level dialog host or a second dialog service — the page's own `ContentDialog` covers the
  page, which is everything below the repository strip and right of the navigation rail.
- Remembering the dialog's size or position, or letting the reader resize the card itself.
- The Changes page's own layout, which has its own panels and was not part of the request.
- Opening the dialog from a keyboard gesture or a tap on an already-selected row: both would take
  the second click of a double-click away from the row's checkout.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the dialog lives | A `ContentDialog` inside `HistoryPageView` | No new service, no window plumbing, and the page keeps its own state; the scrim covers the page, which is what a reader is looking at | the shared window host (its size properties are not reset between dialogs, so this card's size would leak into every confirmation); a second window-level host plus a new dialog service (real infrastructure for one page) |
| How it is sized | `DialogWidth`/`DialogHeight` **and** their maxima bound to 94% of the page | Follows the window live, leaves an inset so it still reads as a dialog, and needs no layout code; the maxima must move with it or the control's own 600 clamps the card | a fixed size (does not resize); auto-size (a diff has no natural width); 100% (a dialog flush to the edges is a panel) |
| How it opens and closes | The ViewModel's `IsDiffDialogOpen`, with the view calling `ShowAsync`/`HideAsync` and the control's `Closed` writing `false` back | Uses the control the way it is built, keeps the state testable without a window, and makes the cross, the Close button, Escape and the scrim all mean the same thing | binding `IsOpen` two-way (re-enters a completion source the control has already resolved) |
| What closing does to the selection | Leaves it | `SelectedCommit` is the start point "create a branch/tag here" falls back to, and the highlight is where the reader is | clearing the selection (loses both, for no gain) |
| Reopening the same row | A context-menu item | Any tap-to-open rule gives the modal the second click of a double-click and silently kills checkout-on-double-click | a tap on the selected row; Enter; a toolbar button for a per-row action |
| The cross | At the right of the dialog's own header row, with the title above it | The control draws the title itself and offers no close glyph, so the cross belongs to the content; the reader still finds it at the top right of the card | a custom control template (a fork of the library's theme for one glyph) |
| Both close affordances | The cross and the bottom Close button | The draft asks for either or both, and the button costs one attribute since the control draws it | one of the two |
| The bottom panel's height machinery | Deleted with the panel | `DefaultDetailsHeight`, `MinimumDetailsHeight` and `ApplyDetailsHeight` exist only to open and collapse a row that no longer exists | keeping it behind the dialog "in case" |
