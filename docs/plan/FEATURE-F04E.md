# FEATURE-F04E — Diffs take the whole page

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-20-ui-polish-diff-page

## Objective

Show what a commit changed on the whole page instead of in a card drawn over it, and let Escape
leave it the moment it opens.

## Context & constraints

- FEATURE-2288 moved the diffs out of a docked panel and into a `ContentDialog`
  (`Views/Pages/HistoryPageView.axaml`), sized at 94 % of the page by `Controls/DialogSizing.cs`.
  The request is now the next step: the diffs take the page.
- The page's root is a `Panel` holding the history `DockPanel` and, over it, the dialog. A panel
  drawn last in that `Panel` covers the page exactly, which is what "the whole page content" means
  here — the shell's rail and repository strip stay where they are.
- The history list must keep its scroll position while the diffs are up: a reader closes the diffs
  to carry on where they were. Collapsing the `DockPanel` re-measures the list at zero and clamps
  its offset, so the diffs are drawn **over** a page that is still laid out.
- `BUG-1AEA` had to force `ScrollBarVisibility.Disabled` on the dialog card's own `ScrollViewer`
  (`OnDialogBodyAttached`), because a scrolling card measures its child with infinite height and
  gave the patch an 84 000 px layout. A panel inside the page has a real height, so that workaround
  goes with the dialog.
- Escape today is the dialog's, and the dialog only hears it once something inside it has focus —
  which is the complaint. A panel has to ask for the key itself: a handler at the page root, and
  focus moved into the panel when it opens.
- `HistoryPageViewModel` already owns the state (`IsDiffDialogOpen`, `CloseDiffDialogCommand`,
  `SelectedSubject/Author/Date/Sha/Body`, `Files`, `Diff`), and `OnShowChanges` is the one way in.
  What changes is the name and the control it drives, not the flow.
- `Controls/DialogSizing.cs` exists only for this dialog and has no other caller.
- The tests that drive the dialog are a whole section of `tests/.../HistoryPageTests.cs`
  (`DiffDialog_*`, `ShowChanges_*`, `DialogSizing_*`), and they are the specification of the
  behaviour that must survive the move.
- **Baseline:** clean build, 1728 tests green.

## PHASE01 — The diff view replaces the history

**Branch:** `feature/feature-f04e-phase01-full-page-diff`
**Status:** TODO

### Steps

1. `ViewModels/Pages/HistoryPageViewModel.cs`: `IsDiffDialogOpen` → `IsDiffViewOpen` and
   `CloseDiffDialogCommand` → `CloseDiffViewCommand`, with the documentation saying what the state
   now drives. No change to when it is set or cleared.
2. `Views/Pages/HistoryPageView.axaml`: the `ContentDialog` is replaced by a `Border` named
   `DiffPage`, last child of the page's root `Panel`, filling it, painted
   `EnigmaBackgroundBrush` and visible only while `IsDiffViewOpen`. It carries a header strip — a
   back button, the commit's subject, then its author, date, short hash and body — and, under it,
   the `ChangedFilesPanelView` / `GridSplitter` / `DiffViewerView` layout the dialog held, unchanged.
3. Same file: the history `DockPanel` stays visible and laid out underneath; the panel's own
   background is what hides it.
4. `Views/Pages/HistoryPageView.axaml.cs`: the dialog's plumbing goes — `ShowAsync`/`HideAsync`, the
   `Closed` handler and `OnDialogBodyAttached` with its card-scrolling workaround. What is left is
   the columns, the viewport reporting and the double-click.
5. Delete `Controls/DialogSizing.cs`: its only caller was the dialog.
6. Tests — `tests/.../HistoryPageTests.cs`: the `DiffDialog_*` and `ShowChanges_*` sections become
   `DiffView_*`, driving `IsDiffViewOpen` and the `DiffPage` panel's visibility; the sizing tests
   become "the panel is the size of the page"; `DialogSizing_TakesMostOfWhatItIsGiven` goes with the
   class. The pane-scrolling test keeps its assertions — each pane still has its own viewport and
   its own bar — against the panel instead of the card.

### Acceptance criteria

- Asking for a commit's changes — by double-click or from the row's menu — fills the page with the
  file list and the diff, hiding the graph, the history toolbar and the column header.
- The header of that page names the commit: subject, author, date, short hash and body.
- The back button and the close command put the graph back, with its scroll position and its
  selection exactly as they were.
- The file list and the diff each scroll themselves; neither scrolls the other, and nothing scrolls
  the page.
- Asking for another commit's changes while the view is open moves it onto that commit.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — Escape leaves the diff at once

**Branch:** `feature/feature-f04e-phase02-escape-closes-diff`
**Status:** TODO

### Steps

1. `Views/Pages/HistoryPageView.axaml`: the `DiffPage` panel is focusable, so a key pressed with
   nothing else clicked has somewhere to come from.
2. `Views/Pages/HistoryPageView.axaml.cs`: a tunnelling `KeyDown` handler at the page root closes
   the view on `Escape` while it is open and marks the key handled, so no child — the file filter
   box, the diff list — can swallow it first; when the view is closed the key is left alone.
3. Same file: opening the view moves focus into it, posted after the layout pass that realises it,
   so the panel is focusable by the time focus is asked for.
4. Tests — `tests/.../HistoryPageTests.cs`: opening the view puts focus inside it; `Escape` raised
   at the page root closes it without anything having been clicked; `Escape` with the view closed
   does nothing; the selection survives the close.

### Acceptance criteria

- Escape closes the diff view immediately after it opens, with no click anywhere first.
- Escape closes it whatever has focus inside it, including the file filter box.
- Escape does nothing on the history page when the diff view is closed.
- Closing with Escape keeps the selected commit, as every other way out does.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- A navigation-rail entry for the diffs, temporary or otherwise.
- Showing the same full-page diff from the changes page, which has its own layout.
- Remembering the splitter position between commits or between sessions.
- Any change to what the diff viewer or the changed-files panel render.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the diffs live | A panel filling the history page, drawn over it | It is the page's own state, it needs no rail entry that would be empty most of the time, and the graph keeps its scroll position underneath | A temporary navigation item (a rail entry that exists for one commit); replacing the page in the shell's `ContentControl` (rebuilds the history page and loses the reader's place) |
| The history underneath | Left visible and laid out, covered by an opaque panel | Collapsing it re-measures the list at zero height and clamps its scroll offset — the reader would come back to the top of the graph | `IsVisible="False"` on the history `DockPanel` |
| The way out | A back button in the header, plus the existing close command and Escape | A full page needs a way back where a dialog had a cross; the command is already bound everywhere else | Only Escape (undiscoverable); keeping a cross in the corner (reads as a dialog) |
| Escape's handler | Tunnelling at the page root | The complaint is that Escape only worked once something specific had focus; tunnelling means it never depends on what has | Bubbling (a focused `TextBox` or list can handle it first); a `KeyBinding` (fires only when focus is already inside) |
| `DialogSizing` | Deleted with the dialog | Its whole purpose was the card's fraction of the page; a panel fills the page by being a panel | Keeping it unused |
| The state's name | `IsDiffViewOpen` | The state now says whether a view is on screen, not a dialog; a name that lies is the next reader's bug | Keeping `IsDiffDialogOpen` |
