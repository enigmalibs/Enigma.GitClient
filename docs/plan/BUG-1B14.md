# BUG-1B14 — Folders in the files panel never select

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-1b14-folders-never-selected`
**Run:** vibe/2026-10-08-file-panel-about-release

## Objective

In the files panel beside the history, a folder is never selected. A click on a folder line folds or
unfolds it, and the file that was selected stays selected, with its diff. A click on a file always
selects it on the first try, whatever was selected before.

## Context & constraints

- Reported behaviour, with a history line selected:
  - a folder is clicked: only the folder is selected and the graph stays. That part is right.
  - then a file is clicked: the file is **not** selected and the folder loses its selection. A second
    click is needed to open the file's diff.
  - with a file selected (its diff open), a folder is clicked: the diff page stays, showing "Select
    a file to see what changed in it." Getting back to a diff takes a click to unselect the folder,
    then a click on a file.
- The first click is lost because of the panel's view. `ChangedFilesPanelView` holds a `ListBox` (list
  mode) and a `TreeView` (tree mode). Only one is visible, but **both** bind `SelectedItem` two-way to
  `SelectedNode`.
  - In tree mode, the hidden `ListBox` holds only the top-level rows.
  - When a nested file is picked while a top-level row (a folder, or a root file) is selected, the
    `ListBox` cannot select the file. It drops its own selection to `null` and writes that `null` back
    over the tree's choice.
  - Picking a nested file from nothing works, because the `ListBox` goes from `null` to `null` and
    writes nothing.
- "Select a file…" over the diff page has a simpler cause. A folder selection is a selection with no
  file: the history clears the diff, but nothing closes the diff page.
- The same `ChangedFilesPanelView` serves a commit's files and both halves of the working tree
  (`UnstagedFiles` / `StagedFiles`), so the fix applies to all three.
- FEATURE-A35A made a selected line let go on a plain click (`ToggleSelection`), and its tests cover a
  folder letting go too. With folders never selected, that folder case goes away.
- A folder line keeps its right-click menu ("Copy path", …) and its chevron.

## Steps

1. `ChangedFilesPanelViewModel.SelectedNode`: a directory row is refused. The selection stays where it
   was, no `SelectionChanged` is raised, and the property is re-announced so that the control that
   offered the folder falls back to the real selection.
2. `ChangedFilesPanelViewModel`: two projections of the selection, one per control.
   - `ListSelection` is `SelectedNode` in list mode and `null` in tree mode. Its setter is ignored
     outside list mode.
   - `TreeSelection` is the same for tree mode.
   - Both are re-announced whenever `SelectedNode` or `ViewMode` changes.
3. `ChangedFilesPanelView.axaml`: the `ListBox` binds `ListSelection`, and the `TreeView` binds
   `TreeSelection`. The hidden control can then never write over the visible one.
4. `ChangedFilesPanelView.axaml.cs`: a plain left press on a folder line, outside its chevron and
   buttons, folds or unfolds the folder and is handled, so the tree never selects it.
   - A double-click folds or unfolds it once, not twice.
   - A right-click still opens the line's menu.
5. Tests:
   - `ChangedFilesPanelTests` (view model):
     - a folder cannot be selected, and the file selected before stays selected (replaces
       `Panel_SelectingADirectoryRowSelectsNoFile`);
     - each projection is `null`, and ignores writes, outside its own mode.
   - A repository-free headless view test of the panel:
     - a click on a folder folds it and selects nothing;
     - with a file selected, a click on a folder keeps the file;
     - a root file, then a nested file: the nested file is selected on the first click;
     - a double-click on a folder folds it once;
     - the keyboard does not select a folder.
   - `FileListToggleTests`: the tree test's folder half asserts the new behaviour, and the history keeps
     a file's diff open when a folder is clicked.

## Acceptance criteria

- A click on a folder line, in a commit's files or in either working-tree half, never selects it. It
  folds or unfolds the folder.
- The file selected before a folder click stays selected, and its diff stays open.
- From any selection, one click on a file, at the root or nested, selects it and opens its diff.
- A folder's right-click menu still opens.
- Build clean with zero warnings; the affected suites are green.

## Out of scope

- Multi-selection, and keyboard shortcuts for folding.
- Any change to the list mode's look.
- Fixing the Desktop test teardown on Windows (BUG-6EAA, abandoned at your request).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Can folders be made non-selectable? | Yes: a click folds or unfolds; the view model refuses a folder from any source | Not hard, and it removes every "folder selected" state the report describes | Keeping folders selectable and patching each reported path |
| What a click on a folder does | Folds or unfolds it | A non-selectable line that does nothing feels dead; it is what GitKraken and VS Code do | Nothing (only the chevron folds) |
| Where it applies | A commit's files and both working-tree halves | One control; three behaviours would be a bug in themselves | The commit's files only |
| The lost first click | Mode-gated selection projections, one per control | It is the root cause, and it also hits a root file followed by a nested file | Re-creating the controls per mode (a larger XAML change) |
| A folder from the keyboard or from code | Refused in the view model's setter | One rule, wherever the selection comes from | Guarding the pointer only |
| Verifying the repository-based Desktop tests on Windows | A temporary, never-committed Windows-safe teardown in `TestServices`, reverted by hand and reported | Their teardown fails on Windows and can mask an assertion (BUG-6EAA); new tests avoid repositories where they can | Not running them; committing the teardown fix (you abandoned BUG-6EAA as out of scope) |
