# FEATURE-8E87 — Compact ref rows with multi-select

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-08-title-watcher-refs-release

## Objective

The Branches, Tags and Remotes pages use tall two-line rows with badges, metadata and buttons. Make
them look like the changed-files panel (`Views/Panels/ChangedFilesPanelView.axaml`), and let the reader
select and delete several items at once.

## Context & constraints

- **The changed-files panel's row** (`ChangedFileRow`):
  - a `Grid` 22 px high, `ColumnSpacing="6"`, with a transparent background so it is hit-testable;
  - column 0: a `Border.status` chip, or a dim `Folder` icon for a folder;
  - column 1: the name (`EnigmaForegroundBrush`, trimmed);
  - the right-hand columns: faint 11 px text, the line counts.

  Its tree is a `TreeView` with `Padding="0,2,18,2"`, no border and a transparent background. Its list
  is a `ListBox`. Both use the theme's own item hover and selection.
- **The panel's folder behaviour:**
  - a click on a folder's line folds it, and never selects it (BUG-1B14);
  - the tree's keyboard selection of a folder is refused;
  - a double-click folds once;
  - expansion is remembered by path across refreshes (BUG-6DB7).

  Expand all / collapse all came with FEATURE-E2D2.
- **The three pages** are shown as dialogs over the history (`ToolDialogService`).
  - Each row view model carries its page's commands, so a context menu (its own popup tree) can bind
    them.
  - Each page rebuilds its rows on every refresh and keystroke, and restores the selection by name.
- **Branches** (`BranchesPageViewModel`):
  - today a flat list of group headings and rows (`Items`), grouped as `Groups`;
  - a drag of one row onto another offers a merge (`BranchesPageView`'s code-behind, pointer-driven,
    on `ListBoxItem`);
  - a manual-merge panel;
  - a sort (name or date, either way) remembered in the settings.
- **Deletes today, one item each:**
  - `IBranchOperations.DeleteAsync(name, isRemote)`:
    - a local branch reads `IsMergedAsync`, then names the unmerged commits (up to 10 subjects) and
      deletes with `-D` after confirming;
    - a remote branch confirms "…This changes the remote for everyone who uses it." and runs
      `push --delete`;
  - `ITagOperations.DeleteAsync` / `DeleteRemoteAsync`: the remote one checks the push guard first;
  - the remotes page's own remove;
  - the history's badge menus call the same `DeleteAsync` methods.
- **`ContentDialogServiceExtensions.ConfirmDestructiveAsync`** is the house red confirmation. Its
  harmless button is the default.
- **Avalonia 12** (checked by decompiling 12.1.1):
  - `ListBox` and `TreeView` with `SelectionMode="Multiple"` give Ctrl+click toggle, Shift+click
    range and Ctrl+A natively;
  - a right-click outside the selection selects that row, and a right-click inside keeps the
    selection;
  - `TreeView.SelectedItems` can be bound to a view-model list;
  - `TreeView.SelectAll` selects every realised item, folders included.

## Shared rules (all phases)

- **Rows look exactly like the changed-files panel's rows:** the same height, spacing, fonts, hover
  and selection, and the same folder icon and expander in the tree.
  - The panel's values move into shared styles in `Themes/Styles.axaml`, and the panel and the three
    pages all use them. No value is copied.
  - A branch, tag or remote row shows a **dim icon for its kind** where a file row shows its status
    chip, so names line up with folder names.
- **Removed:** per-row buttons, pills, the second line and the row separators. Actions go in the
  context menu, the page header and the keyboard.
- **What leaves the row goes in the row's tooltip:**
  - a branch: its upstream, last commit subject, author and date;
  - a tag: its kind, message, short SHA, tagger and date.
- **Layouts:** branches are always a tree; tags and remotes are always a flat list. No list/tree
  toggle on any page.
- **Kept:** each page's header (title, filter, sort, *New …*), and the manual-merge panel on the
  Branches page.

## PHASE01 — Shared rows, compact tags and remotes

**Branch:** `feature/feature-8e87-phase01-compact-tags-remotes`
**Status:** DONE — see `docs/done/FEATURE-8E87-PHASE01.md`

### Steps

1. `Themes/Styles.axaml`: the changed-files panel's row and container values as shared classes:
   - the row grid: height, spacing, a transparent background;
   - the name text;
   - the right-hand faint text;
   - the list and tree containers: border, background, the tree's padding.

   `ChangedFilesPanelView` uses them, and draws exactly as before.
2. **Tags page:**
   - **Row:** a dim `Tag` icon, then the name. The tooltip carries the kind, the message (when there
     is one), the short SHA, the tagger (when there is one) and the date.
   - **Menu:** *Select in the history*, *Check out (detaches HEAD)*, *Push to the remote*,
     *Delete locally…*, *Delete from the remote…*.
   - The page's own `ListBoxItem` style, the pills and the buttons go.
3. **Remotes page:**
   - **Row:** a dim `HardDrives` icon and the name on the left; the fetch URL on the right, faint,
     trimmed with an ellipsis. The tooltip carries the host, and the push URL when it differs.
   - **Menu:** *Fetch*, *Edit…*, *Remove…*.
4. The row view models expose the tooltip text, built and unit-tested in the view model.
5. Update the tests that read the old rows. Add tests for the tooltips, and for the rows using the
   shared classes.

### Acceptance criteria

- The tags and remotes rows are 22 px rows in the changed-files panel's style, with no button, pill,
  second line or separator. The tooltips carry what left the row.
- The changed-files panel looks exactly as before, now through the shared styles.
- Build: zero warnings. The whole suite is green.

## PHASE02 — Bulk delete for tags and remotes

**Branch:** `feature/feature-8e87-phase02-bulk-delete-tags-remotes`
**Status:** DONE — see `docs/done/FEATURE-8E87-PHASE02.md`

### Steps

1. **Both lists** take `SelectionMode="Multiple"`, with their selection bound to the page.
   - Ctrl+click adds or removes, Shift+click selects a range, Ctrl+A selects every visible item.
   - A right-click outside the selection selects that row first; inside, it keeps the selection.
2. **The menus:**
   - with several items selected, they show only the delete actions: *Delete locally…* and
     *Delete from the remote…* for tags, *Remove…* for remotes;
   - the single-item actions (*Select in the history*, *Check out*, *Push*, *Fetch*, *Edit*) show only
     with exactly one item selected.
3. **The header:**
   - a *Delete (n)* button on Tags (deletes locally) and a *Remove (n)* button on Remotes;
   - disabled when nothing is selected;
   - the Delete key on the list does the same.
4. **`Services/BulkDeletion.cs`**, the shared, pure model of a batch:
   - every item with its name, its detail lines (for example the commits it would lose), whether it
     changes a remote, and why it is skipped, if it is;
   - the confirmation: title, text, and the remote warning, in the existing wording ("This changes the
     remote for everyone who uses it.");
   - the outcome and its summary: deleted, failed with reasons, skipped with reasons, and the
     severity (success, warning when partial, error when nothing went).
5. **`ITagOperations`:**
   - `DeleteAsync(IReadOnlyList<string>)` and `DeleteRemoteAsync(IReadOnlyList<string>)`, the remote
     one checking the push guard once;
   - one red confirmation listing every tag;
   - one exclusive section for the batch, with every item tried, a failure recorded and the batch
     going on;
   - one refresh at the end and one info-bar summary.

   The single-name methods become a batch of one, with today's wording.
6. **Remotes:** the same flow for removing several, with each remote's tracking-branch count in the
   confirmation ("Nothing on the remote itself is touched").
7. Tests:
   - the confirmation's contents (local tags; remote tags with the warning; remotes);
   - the summary after a partial failure;
   - one refresh per batch;
   - a single delete through the same path;
   - the menus' visibility by selection count;
   - the button's label, and that it is disabled with nothing selected;
   - the Delete key.

### Acceptance criteria

- Several tags or remotes can be selected and deleted with one confirmation listing every one. A
  failed item does not stop the others, and one summary says what went and what did not, and why.
- One refresh per batch.
- Build: zero warnings. The whole suite is green.

## PHASE03 — Branches as a compact tree

**Branch:** `feature/feature-8e87-phase03-branch-tree`
**Status:** DONE

### Steps

1. **`ViewModels/Pages/BranchTree.cs`:**
   - the tree's nodes: a top-level node (*Local*, or one remote), a folder, and the branch row as
     the leaf;
   - a pure `BranchTreeBuilder`:
     - *Local* first, then one node per remote, alphabetical;
     - inside each, every `/` segment of a name is a folder;
     - a chain of folders that each have a single child merges into one node, as `FileTreeBuilder`
       does (`vibe/2026-10-08` is one node);
     - folders first, by name; branches after, in the page's sort order and direction;
     - the filter keeps the matching branches and their parent folders, expanded.
2. **The page:**
   - `Roots` replaces the flat `Items`;
   - top-level nodes start expanded and folders collapsed;
   - expansion is remembered by node key across refreshes and forgotten with the repository;
   - the selection is restored by name.
3. **The branch row:**
   - the last name segment, after a dim `GitBranch` icon;
   - small badges in the right-hand column, each only when it applies, in this order:
     *checked out*; *hidden* with an `EyeSlash` icon; ahead `↑n`; behind `↓n`; *local only*;
   - every badge has a tooltip saying what it means;
   - no bold text, no dimming, no "upstream gone" or "published" marker;
   - the row's tooltip carries the upstream, the last commit's subject, its author and its date.
4. **The folder's line** behaves as in the changed-files panel: a click folds it and never selects
   it, the keyboard's selection of a folder is refused, and a double-click folds once.
5. **Drag-to-merge** works on the tree's rows. Folders and top-level nodes are never drop targets.
6. **The menu** keeps today's items.
7. Tests:
   - the builder: top-level nodes, folders, merged folder chains, sort and direction, filter;
   - expansion surviving a refresh;
   - the badges' order and tooltips;
   - the row tooltip;
   - the existing page and drag tests, moved to the tree.

### Acceptance criteria

- The branches page is a tree of 22 px rows in the changed-files style:
  - *Local* first, then the remotes;
  - folders with merged chains;
  - the five badges in order with their tooltips.
- Filtering expands the paths to the matches. Expansion survives a refresh. Dragging a branch onto
  another still offers the merge.
- Build: zero warnings. The whole suite is green.

## PHASE04 — Bulk delete for branches

**Branch:** `feature/feature-8e87-phase04-bulk-delete-branches`
**Status:** TODO

### Steps

1. **The tree** takes `SelectionMode="Multiple"`, its `SelectedItems` bound to the page.
   - The page acts only on branch rows: a top-level node or a folder can take focus, but is never
     part of the selection, so selecting a folder deletes nothing.
   - The view takes any folder back out of the tree's selection, and keeps the branches selected
     before when the tree selected only a folder (BUG-1B14's pattern).
   - Ctrl+A selects every visible branch, inside expanded nodes only.
2. **The menu:**
   - with several branches, only *Delete…*;
   - with exactly one, today's items (*Select in the history*, *Check out*, *Merge*, *Hide/Show in the
     history*, *Rename*, *Set upstream*, *Delete*).
3. **The header:** a *Delete (n)* button, disabled with nothing selected; the Delete key does the
   same.
4. **`IBranchOperations.DeleteAsync(IReadOnlyList<BranchToDelete>)`:**
   - **One confirmation** lists every branch:
     - each unmerged local branch with the commits it would lose (up to 10, then "…and n more");
     - when remote branches are included, the warning that this changes the remote for everyone;
     - the checked-out branch marked *skipped* and never attempted.
   - **One exclusive section:** `-d` or `-D` per local branch (`-D` only for one confirmed as
     unmerged), `push --delete` per remote branch. A failure is recorded and the batch goes on.
   - **One refresh** at the end, and **one summary**.
   - `DeleteAsync(name, isRemote)` becomes a batch of one with today's wording. The history's badge
     menu and the page's single delete both go through it.
5. Tests:
   - the confirmation's contents (unmerged commits listed, the remote warning, the checked-out branch
     skipped);
   - the summary after a partial failure;
   - one refresh per batch;
   - a selection holding a folder deletes only branches;
   - the menu by selection count;
   - the button and the Delete key;
   - the single delete through the same path.

### Acceptance criteria

- Several branches, local and remote, can be selected and deleted with one confirmation that names
  what would be lost and what is skipped.
- A failure does not stop the batch, and one summary reports what happened, with one refresh.
- Build: zero warnings. The whole suite is green.

## Out of scope

- A list/tree toggle on any page.
- Bulk actions other than delete (bulk push, bulk hide).
- A second-line or metadata column option.
- Remembering the tree's expansion across sessions.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Reusing the panel's look | Shared classes in `Themes/Styles.axaml`, used by the panel and the pages | "Reuse, don't copy": one place to change a row | Copying the values; a shared `UserControl` row (the rows' content differs) |
| Multi-select mechanics | Avalonia's native `SelectionMode="Multiple"` | Ctrl, Shift, Ctrl+A and the right-click rules are built in (verified in 12.1.1) | A hand-made selection over pointer events |
| Folders in the selection | The view refuses them; the page counts only branch rows | Matches BUG-1B14's refusal and guarantees "a folder deletes nothing" in the view model, where it is testable | Letting folders select and ignoring them (a highlighted folder that does nothing) |
| Ctrl+A in the tree | Visible branches only (inside expanded nodes) | "Every visible item", and never a branch the reader cannot see | Every branch matching the filter |
| Header button wording | *Delete (n)* on Branches and Tags, *Remove (n)* on Remotes | Each page keeps its own verb: a remote is removed here, nothing is deleted on it | *Delete (n)* everywhere |
| Tags' *Delete (n)* and Delete key | Delete locally | The safe, local half; the remote half stays an explicit menu item with its warning | Asking which, every time |
| Ahead/behind badges | `ArrowUp`/`ArrowDown` icons with the count, as faint text | The house rule: icons follow the theme and the scale, unlike a font's arrows | `↑`/`↓` characters |
| Badges' look | Faint text and icons in the right-hand column, no pill | "No pills", and the column where file rows show line counts | Keeping the pill borders |
| Folder start state | Top-level expanded, folders collapsed | The draft names top-level nodes only, and a long `feature/` folder stays out of the way | Everything expanded |
| Folder order | By name, case-insensitive, ascending whatever the direction | "Folders come first, sorted by name"; the direction is the branches' | Folders following the direction |
| Running a batch | One exclusive section and one refresh | "Refresh once at the end": the auto refresh cannot slip between items | One `RunExclusiveAsync` per item with `refreshAfter: false` |
| A single delete | A batch of one, with today's wording | "The same path", and the history's badge deletes come along | Keeping two paths |
| The summary | One info-bar notification: success, warning when partial, error when nothing went | The house's way to report an operation | A dialog |
| Breakdown | Tags and remotes first (shared styles, then the batch model), branches after | The flat lists establish the shared row and the batch model the tree then reuses | Branches first |
