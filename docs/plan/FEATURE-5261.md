# FEATURE-5261 — History details panel

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** vibe/2026-09-30-history-details-panel

## Objective

A GitKraken-style panel on the right of the history:

- for a commit line, the panel lists the files the commit changed (the list the diff view shows on
  its left today);
- for the uncommitted line, it holds what the Changes page's left column holds: what is not staged,
  what is staged, and the commit form;
- the lines are toggles: a click on an unselected line selects it and opens the panel; a click on the
  selected line unselects it and closes the panel;
- the Graph, Refs, Author, Date and Commit columns keep their widths, and the Message column gets
  narrower while the panel is open;
- the Changes page goes away.

## Context & constraints

- **The history page.** `ViewModels/Pages/HistoryPageViewModel.cs` and `Views/Pages/HistoryPageView.axaml(.cs)`:
  - `SelectedRow` is two-way bound to the `CommitList` `ListBox`. Selecting a line loads its files
    into `Files` (a `ChangedFilesPanelViewModel`). The uncommitted line uses `DiffTarget.Uncommitted()`,
    a commit uses `DiffTarget.Commit(sha)`;
  - `IsDiffViewOpen` shows `DiffPage`, a full-page overlay with a one-line header (back, commit
    details, subject), the file list (`Files`) and the `DiffViewerView` (`Diff`). It opens on the
    first file, from a double-click (`RowCommands.Activate`, wired by `OnCommitDoubleTapped`) or from
    the line's menu (`RowCommands.ShowChanges`, "Show what it changed");
  - activating the uncommitted line raises `WorkingDirectoryRequested`, and `MainWindowViewModel`
    answers by going to `ShellPage.Changes`;
  - an automatic refresh (`RefreshInPlaceAsync`) is deferred while the diff is open
    (`_refreshPending`). `ReloadKeepingPlaceAsync` replaces every row, so the `ListBox` pushes
    `SelectedRow = null` before the same line is selected again;
  - the page already routes tunnelled pointer events itself, for dragging a branch badge onto another.
- **The columns.** `HistoryColumnLayout` gives every row and the header the same widths, measured
  against the list's viewport (`Columns.Viewport`, reported from the list's `ScrollViewer` on size
  and scroll changes). The message is the `*` column (minimum 120). A narrower list therefore means a
  narrower message, with no change to the other columns.
- **The Changes page.** `ViewModels/Pages/ChangesPageViewModel.cs` and `Views/Pages/ChangesPageView.axaml(.cs)`.
  Two `ChangedFilesPanelViewModel`s (`Unstaged`, `Staged`), only one of them selected at a time; the
  commands to stage, unstage and discard (a file, a directory, or everything), stash everything and
  commit; the subject-length and body-width guides; Ctrl+Enter commits. There is also a stash list
  (`StashRowViewModel`, `StashFiles`), a header with the branch and its upstream, a back button, and
  Escape going back to the history. It is registered in DI, on the rail (`ShellNavigation`) and in
  `MainWindowViewModel`.
- **The diff viewer** is registered transient ("per consumer"). The history's `Diff` is the one the
  overlay shows.
- **Tests** (xUnit v3, headless Avalonia, real repositories): `HistoryPageTests` (the diff view:
  `Activate`, `ShowChanges`, Escape, focus, header), `ChangedFilesPanelTests`, `DiffViewerTests`,
  `AutoRefreshTests`, `BranchVisibilityTests`, `TagsAndCheckoutTests`, `WholeLineMenuTests`,
  `ChangesPageTests`, `StashPanelTests`, `MenuIconTests`, `ShellRenderTests`, `CompositionRootTests`,
  `MainWindowShellTests` (the rail's items). `window.MouseDown/MouseUp` drive the pointer.
- **User-facing text naming the Changes page:** the empty history ("Make one from the Changes page"),
  `StashOperations`' conflict message ("Resolve them on the Changes page"), the Settings page's
  file-list description, and the README's feature lines.

## PHASE01 — A details panel for commits

**Branch:** `feature/feature-5261-phase01-commit-panel`
**Status:** DONE — see `docs/done/FEATURE-5261-PHASE01.md`

### Steps

1. `HistoryPageViewModel`:
   - `IsDetailsPanelOpen`: true while a line is selected. It stays true through an in-place reload
     (`ReloadKeepingPlaceAsync`), which must not close and reopen the panel, or empty its file list,
     for the line it is about to select again;
   - `DetailsPanelWidth` (default 380, from 280 to 720) and `ResizeDetailsPanel(delta)`, as the
     column grips resize their columns;
   - `CloseDetailsPanelCommand`: unselects the line;
   - `ToggleRow(row)`: what a plain click on a line does. The selected line is unselected; any other
     line is left to the list, which selects it;
   - the diffs open when a file is selected in the panel (a user's click, or "Show what it changed",
     which still selects the line and opens its first file). Selecting a line never opens them by
     itself. Closing the diffs clears the panel's file selection, so the same file can be clicked
     again;
   - remove `Activate` from `HistoryRowCommands` (the double-click). For this phase, the uncommitted
     line's panel lists `DiffTarget.Uncommitted()` read-only, as `Files` did before.
2. `HistoryPageView.axaml`:
   - the page becomes the existing content (toolbar, header, list, and the diff overlay drawn over
     them) on the left, and the details panel docked on the right, over the page's whole height;
   - the panel has a header (the line's subject; the commit-details button, for commits only; a close
     button) above `ChangedFilesPanelView` bound to `Files`;
   - a grip on the panel's left edge resizes it;
   - the overlay no longer carries a file list of its own: its header and the `DiffViewerView`.
3. `HistoryPageView.axaml.cs`:
   - remove `OnCommitDoubleTapped`;
   - a left press with no modifier on the selected line, released on it without a drag, is a toggle
     (`ToggleRow`). The second press of a double-click, a right-click and a badge drag are not;
   - when the diffs open while the focus is in the panel, the focus stays there; otherwise the overlay
     takes it, as it does today. Escape still closes the diffs from anywhere on the page.
4. `Themes/Styles.axaml`: the panel grip's look (a 4-wide rule, like the page's splitters, with a
   west-east cursor).
5. Tests — rewrite the diff-view tests that went through `Activate`, and add:
   - a click on an unselected line opens the panel with that commit's files; a click on the same line
     closes it and unselects; a click on another line moves the panel onto it;
   - a double-click on an unselected line leaves it selected, with the panel open;
   - the close button closes the panel and unselects;
   - with the panel open, the list's viewport and the message column are narrower, and the graph,
     refs, author, date and commit widths are unchanged;
   - clicking a file in the panel opens its diff over the graph, beside the panel. Back and Escape
     close the diff and keep the line and the panel;
   - the panel stays open, on the same line, through an in-place refresh, and closes when its line
     is gone;
   - the grip resizes the panel within its bounds.

### Acceptance criteria

- One click on a line opens the right panel with the files it changed; a second click on the same
  line unselects it and closes the panel. A double-click never leaves the panel closed.
- A file clicked in the panel shows its diff over the graph, with the panel still beside it. Back and
  Escape bring the graph back and keep the selection and the panel.
- With the panel open, the Graph, Refs, Author, Date and Commit columns have the widths they had, and
  the Message column is narrower.
- "Show what it changed" and "Show commit details" still work from the line's menu.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The working tree in the panel

**Branch:** `feature/feature-5261-phase02-working-tree-panel`
**Status:** TODO

### Steps

1. `ViewModels/Pages/ChangesPageViewModel.cs` → `ViewModels/Panels/WorkingTreePanelViewModel.cs`:
   - it keeps `Unstaged`, `Staged` (one side selected at a time), the stage, unstage, discard, stash
     and commit commands, the message and its guides;
   - it drops the page: the back button, `HistoryRequested`, `Title`, the branch and upstream header,
     and the stash list (the graph draws every stash as a line, with apply, pop and delete);
   - it no longer owns a diff viewer: it says which file (and which half of the change) is selected,
     and the history shows it in its own `Diff`;
   - it reads the status while the history shows it (the uncommitted line is selected) and on the
     repository's state refreshes while it does. A new repository clears the message;
   - it raises an event after every operation that changed the working tree or HEAD, so the history
     can refresh in place.
2. `Views/Pages/ChangesPageView.axaml(.cs)` → `Views/Panels/WorkingTreePanelView.axaml(.cs)`: the
   left column only (Not staged with Discard all, Stash all and Stage all; Staged with Unstage all;
   the commit box). Ctrl+Enter still commits.
3. `HistoryPageViewModel` / `HistoryPageView`:
   - the uncommitted line's panel is the working-tree panel. The commit lines' panel is unchanged;
   - a file picked in either half opens its diff (`DiffTarget.WorkingTree()` / `DiffTarget.Staged()`)
     over the graph, as PHASE01 does for a commit's files;
   - after an operation from the panel, the history refreshes in place. The uncommitted line stays
     selected while there is uncommitted work; with a clean tree it goes away, and the panel with it;
   - the empty history's message no longer names the Changes page.
4. Remove the page: `ShellPage.Changes`, its rail item, its DI registrations, and
   `MainWindowViewModel`'s wiring (`WorkingDirectoryRequested`, `HistoryRequested`, the `changes`
   parameter).
5. Reword the user-facing text that names the Changes page: `StashOperations`' conflict message, the
   Settings page's file-list description, and the comments that describe the page.
6. Tests:
   - move `ChangesPageTests` to `WorkingTreePanelTests` for what survives (the split into staged and
     not staged, stage, unstage, discard with its red confirmations, commit and its guides,
     Ctrl+Enter, no amend or sign off, the diffs of either half);
   - delete what went with the page: the back button, Escape to the history, the upstream summary,
     and `StashPanelTests` (the page's stash list);
   - add: selecting the uncommitted line opens the working-tree panel; a commit from it adds the
     commit to the history in place; a clean tree after a discard closes the panel; the first commit
     of an unborn repository is made from the panel;
   - update `MenuIconTests`, `WholeLineMenuTests`, `ShellRenderTests`, `CompositionRootTests` and
     `MainWindowShellTests` (the rail is History only, plus its footer).

### Acceptance criteria

- Selecting the uncommitted line opens the panel with Not staged, Staged and the commit form. Staging,
  unstaging, discarding and committing all work there, and a file picked in either list shows its
  diff over the graph.
- A commit made from the panel appears in the history without waiting for the automatic refresh.
- There is no Changes page: not on the rail, not in the container, and not named in any message.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Persisting the panel's width.
- A commit's author, date and hash in the panel (the commit-details dialog has them).
- Amend, sign off, or any new commit option.
- A stash list outside the graph and the remotes dialog.
- Keeping the Message column above its minimum on a window too narrow for every column plus the panel.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Split | One FEATURE, two phases: commit lines first, then the working tree with the page removed | Each phase is one reviewable concern. The page can only go once its content has a new home | One dev (too big to review); three phases with the page and the panel side by side for a while (duplicated working-tree logic) |
| Where a file's diff opens | Over the graph, left of the panel, which stays | GitKraken's layout. The panel is the way between files | The whole page, with the panel hidden; inside the panel (too narrow for a diff) |
| What opens a diff | Clicking a file in the panel | A line click is now the toggle, and a diff on every click would hide the graph the reader is browsing | Opening the first file whenever a line is selected |
| Double-click | Removed. The second press of a double-click never toggles | It was how the files were reached, which the panel replaces. Treating it as two toggles would flash the panel shut out of habit | Keeping it as "open the first diff" (a second gesture fighting the toggle) |
| Toggle-off gesture | A plain left click (press and release, no drag) on the selected line | A badge drag or a right-click on the selected line must not close the panel | Toggling on press; toggling on any button |
| Panel header | Subject, commit details (commits only), close (X) | A visible, keyboard-reachable way to close the panel; the details are one click away as before | Author, date and hash in the panel (the dialog has them) |
| Panel width | 380 by default, 280 to 720 by a grip, not persisted | Room for the working tree's headers and file names; resizable like the file lists beside the diffs | A fixed width; persisting it (a settings change nobody asked for) |
| How the panel is resized | A Thumb on its edge driving a ViewModel width | A `GridSplitter` turns an Auto column into a fixed one, which then stays reserved when the panel closes. The Thumb is the column grips' own pattern | `GridSplitter` with code-behind to restore the column |
| Uncommitted panel content | Not staged, Staged, the commit box | What the draft names (the files and the commit form). The window strip already shows the branch and its upstream, and the graph the stashes | Carrying over the stash list and the branch header |
| Working-tree architecture | `ChangesPageViewModel` becomes `Panels/WorkingTreePanelViewModel`, owned by the history, using the history's diff viewer | Keeps the proven logic and tests, and keeps the 1900-line history ViewModel from absorbing another 600 lines | Merging it into `HistoryPageViewModel`; keeping the page ViewModel without a page |
| Refresh while the panel is open | An in-place reload keeps the panel and its list; the panel closes only when its line is gone | The automatic refresh runs every few seconds, and a panel that blinks shut each time is unusable | Letting the transient null selection close the panel |
| After a panel operation | The history refreshes in place | A commit must appear at once, and a clean tree must drop the uncommitted line | Waiting for the automatic refresh |
| Narrow windows | Message shrinks to its 120 minimum, then the right-hand columns clip | "The other columns stay as they are"; the panel can be made narrower | Squeezing the fixed columns; capping the panel from the page width |
| Focus when a diff opens from the panel | Stays in the panel | Arrow keys move from file to file, as they did in the diff view's own list | Moving it to the overlay (breaks the arrow keys) |
| Escape | Closes the diffs; leaves the panel | Unchanged meaning; the panel has its close button and the toggle | Escape also closing the panel |
