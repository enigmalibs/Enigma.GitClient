# FEATURE-3B62 — Selectable rows and branch drops

**Status:** DONE
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-09-18-columns-selection-minimap

## Objective

Make the branches, tags and remotes lists behave like lists: a row can be selected with the pointer
or the keyboard, and on the branches page one branch can be dragged onto another, which opens a menu
of what that pair can do — the gesture the history page gives up in FEATURE-3030.

## Context & constraints

- Branches, tags and remotes are `ItemsControl`s inside `ScrollViewer`s
  (`Views/Pages/BranchesPageView.axaml`, `Views/Pages/RemotesPageView.axaml`): no selection, no
  keyboard navigation, no focus. Their rows already carry their own commands, so a row reaches the
  page's operations through a plain binding — a `ListBox` changes how a row is presented, not what
  it can do.
- The branches list is two levels: `Groups` (Local, then one per remote) each holding `Rows`. One
  selection for the whole page means one `ListBox` over a flat sequence in which a group heading is
  an item of its own — nested `ListBox`es would each keep their own selection and two rows would be
  highlighted at once.
- Avalonia resolves a template by the item's type when several `DataTemplate`s sit in
  `ListBox.DataTemplates`, so a heading item and a branch item can share one list without a
  selector. A heading must not be selectable, which an `ItemContainerTheme` setter can say by
  binding the container's `IsEnabled` to the item — with a style that keeps a disabled heading at
  full opacity, since it is not disabled in the sense the theme means.
- `BranchesPageViewModel.Rebuild` recreates every row on every refresh, filter keystroke and
  operation. Selection therefore has to be restored by identity — the branch's full name — or it is
  lost on every rebuild.
- `Services/BranchDropOperations.cs` already owns what a drop does: the policy (`CanDrop` — no
  self-drop, never a remote target), the checkout-then-merge composition, and the InfoBar sentence
  for a drop that means nothing. Today it also asks the question, through a `ContentDialog` offering
  Merge / Fast-forward only / Cancel. The request asks for a **context menu** instead, so the
  question moves to the caller and the service is told which mode was chosen.
- `git rebase` is refused at the process boundary (`Core/Git/ForbiddenGitOperations.cs`) as a
  permanent product exclusion, so the menu offers merge variants only.
- `BranchDropTests` drives the service against a real repository and sets `services.Dialogs.Result`
  to choose the mode; those tests pass the mode directly once the dialog is gone.
- **Baseline:** `Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress` fails on
  the branch this run started from, for an environment reason unrelated to this item (git 2.55
  narrates nothing for a fast local fetch). The gate for every phase is the App and Core unit suites
  green and no new failure anywhere.

## PHASE01 — Selectable branch and tag rows

**Branch:** `feature/feature-3b62-phase01-selectable-branches`
**Status:** DONE — see `docs/done/FEATURE-3B62-PHASE01.md`

### Steps

1. `ViewModels/Pages/BranchesPageViewModel.cs`: a flat `Items` collection replacing the nested
   `Groups` binding for the view — a heading item (`BranchGroupHeaderViewModel`, from what
   `BranchGroupViewModel` carries today) followed by its rows — with `SelectedItem`,
   `SelectedBranch` and `SelectedTag`. `Rebuild` restores the selection by name, and clears it when
   the selected branch or tag is gone.
2. Same file: a common shape for the two kinds of item — whether it is selectable — so the view can
   say so without knowing the concrete types.
3. `Views/Pages/BranchesPageView.axaml`: both `ItemsControl`s become `ListBox`es; the branch list
   binds `Items` with a `DataTemplate` per item type and an `ItemContainerTheme` that disables the
   headings' containers; the row templates lose the `Border.branchrow:pointerover` background in
   favour of the theme's own selection and hover visuals, keeping the separator line.
4. Same file: a style keeping a heading container at full opacity and without a pointer cursor, so
   "not selectable" does not read as "disabled".
5. Tests — `tests/.../BranchesPageTests.cs` and `tests/.../TagsAndCheckoutTests.cs`: the flat items
   are heading-then-rows in group order; selecting a row exposes it as `SelectedBranch`; a rebuild
   after a refresh or a filter keeps the selection when the branch is still there and drops it when
   it is not; a heading is not selectable; the rendered page's branch list is a `ListBox` whose
   selection follows the ViewModel.

### Acceptance criteria

- A branch or tag row can be selected by pointer and by keyboard, and exactly one row is selected at
  a time across every group.
- A group heading cannot be selected and does not look disabled.
- Selection survives a refresh, a filter keystroke and an operation when the row still exists, and
  is cleared when it does not.
- Every existing branch and tag operation still runs from the row's menu and its buttons.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE02 — Selectable remote rows

**Branch:** `feature/feature-3b62-phase02-selectable-remotes`
**Status:** DONE — see `docs/done/FEATURE-3B62-PHASE02.md`

### Steps

1. `ViewModels/Pages/RemotesPageViewModel.cs`: `SelectedRemote`, restored by name across the rebuild
   that every add, edit, remove and refresh performs.
2. `Views/Pages/RemotesPageView.axaml`: the `ItemsControl` becomes a `ListBox` bound to `Remotes`
   with `SelectedItem`, keeping the row template, its menu and its buttons; the hand-rolled
   pointer-over background gives way to the theme's.
3. Tests — `tests/.../RemotesAndSyncTests.cs`: selecting a remote exposes it; a refresh keeps the
   selection, removing the selected remote clears it; the rendered page's list is a `ListBox`.

### Acceptance criteria

- A remote row can be selected by pointer and by keyboard.
- The selection survives a refresh and is cleared when the remote is removed.
- Fetch, edit and remove still run from the row.
- Build clean with zero warnings; the App and Core unit suites green.

## PHASE03 — Dropping one branch onto another

**Branch:** `feature/feature-3b62-phase03-branch-drop-menu`
**Status:** DONE — see `docs/done/FEATURE-3B62-PHASE03.md`

### Steps

1. `Services/BranchDropOperations.cs`: `DropAsync(BranchDropRequest request, FastForwardMode mode)`
   — the confirmation dialog goes, since the menu is the question; the refusal path keeps its
   InfoBar sentence, and the checkout-then-merge composition is unchanged.
2. `ViewModels/Pages/BranchesPageViewModel.cs`: `BranchDropRequest.For(source, target)` built from
   two rows, `CanDrop`, and one command per menu action — merge, merge fast-forward-only, and merge
   the other way round when that pair allows it. Each reloads the page through the existing `Run`
   helper.
3. `Views/Pages/BranchesPageView.axaml.cs` (new code-behind body): a press on a branch row starts a
   drag carrying the row, in-process; drag-over marks the row under the pointer as a drop target
   when the pair is one the service would carry out; the drop opens a `ContextMenu` at the pointer
   whose items name both branches ("Merge \"feature\" into \"main\"", the same fast-forward only,
   and the reverse), each running its command.
4. `Views/Pages/BranchesPageView.axaml`: rows accept drops and carry a `droptarget` class;
   `Themes/Styles.axaml` gives that class a ring that reads over both the hover and the selection
   brush.
5. A drop the service refuses — a branch on itself, anything onto a remote branch — opens no menu
   and explains itself through the existing InfoBar path.
6. Tests — `tests/.../BranchDropTests.cs`: the mode is passed rather than answered, and the dialog
   assertions become mode assertions; the refusals still report through the InfoBar and run nothing.
   `tests/.../BranchesPageTests.cs`: the page's drop commands merge the right way round, the reverse
   command is offered only when it is valid, and a refused pair exposes no runnable command.

### Acceptance criteria

- A branch row can be dragged onto another branch row, and the row under the pointer says it is a
  target only when the drop would do something.
- Dropping opens a menu naming both branches, with a merge, a fast-forward-only merge and — when it
  is valid — the merge the other way round.
- Choosing an action checks out the target when it is not already checked out, merges, and refreshes
  the page; dismissing the menu does nothing at all.
- A branch dropped on itself, or any branch dropped on a remote branch, opens no menu and says why.
- Build clean with zero warnings; the App and Core unit suites green.

## Out of scope

- Multi-selection anywhere, and drag-and-drop of more than one row.
- Dropping a tag or a remote anywhere, or dropping a branch onto a remote (still refused).
- Rebase, cherry-pick or push as drop actions — rebase is permanently excluded, and the other two
  are not what dropping a branch on a branch means.
- Persisting the selection between sessions, or a details pane driven by it.
- Setting an upstream by dropping a remote branch onto a local one: the existing
  `SetUpstreamAsync` asks its own question through a dialog, and pre-selecting the dropped branch in
  it is a change to that flow rather than to this one.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How a row becomes selectable | A real `ListBox` | Keyboard navigation, focus and the theme's selection brush come with it, and the drop gesture needs a container to mark anyway | An `IsSelected` flag per row, driven from a `Tapped` handler (re-implements selection, and loses the keyboard) |
| The grouped branch list | Flattened to headings-plus-rows in one list | One selection for the page; nested lists would each keep their own | Nested `ListBox`es with cross-clearing; a `TreeView` (a different interaction than the one asked for) |
| Headings in that list | Items whose container is disabled, restyled to full opacity | Keeps one flat list without inventing a selection filter | A non-item heading outside the list (loses the scroll alignment); allowing headings to be selected |
| Selection across a rebuild | Restored by name | The page recreates its rows on every keystroke of the filter; without this, selection would be impossible to keep | Rebuilding only what changed (a larger change to the page than the request asks for) |
| Where the drop question is asked | A context menu at the pointer, replacing the dialog | It is what the request asks for, and the menu can name several actions where a dialog offered two buttons | Keeping the dialog and adding buttons |
| What the menu offers | Merge, fast-forward-only merge, and the reverse merge when valid | Those are the actions this pair of branches has; the reverse covers the common case of dragging the wrong way round | Adding a rebase (refused at the process boundary); adding push (a different flow, with its own reporting) |
| Who owns the action | `BranchDropOperations`, told the mode | It already owns the checkout-then-merge composition and the refusal message; only the question moves | Re-implementing the composition in the page |
| A refused drop | No menu, the existing InfoBar sentence | The reader gets the same explanation they get today, and a menu of nothing is worse than a sentence | A menu with disabled items |
| Remote targets | Still refused | Writing to a remote-tracking ref is a push, and a push is not a thing to arrive at by dragging | Allowing it, mapped to a push |
