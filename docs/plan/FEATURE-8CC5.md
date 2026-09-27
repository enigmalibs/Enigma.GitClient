# FEATURE-8CC5 — The first file, every time

**Status:** DONE — see `docs/done/FEATURE-8CC5.md`
**Type:** FEATURE
**Branch:** `feature/feature-8cc5-first-file-selected`
**Run:** feature/2026-09-27-diff-profiles-release

## Objective

Opening the diffs of a commit always selects the **first file** the panel lists. The file that was
selected before — in another commit, or in the same commit the last time its diffs were open — is
never selected again, and the diff view never opens with no file selected while the commit has files.

## Context & constraints

- `HistoryPageViewModel` loads the selected row's files as soon as a row is selected
  (`LoadChangedFilesAsync`, asynchronous), whether or not the diff view is open. The view opens on a
  double-click or the row's menu (`OnShowChanges` → `IsDiffViewOpen = true`), usually before or while
  the files arrive.
- `ChangedFilesPanelViewModel.SetFiles` → `Rebuild` keeps the previously selected **path** when the new
  file set has it, and selects nothing otherwise. That is the behaviour this item replaces for the
  history; `Rebuild` also runs on a view-mode switch, a search and a preference change, where keeping
  the selection is right and stays.
- The same panel serves the changes page (staged/unstaged) and the stash panel, which refresh the same
  change in place: they keep their selection, unchanged.
- `DiffTarget` is set before the files arrive. Selecting a file of the *previous* commit against the new
  target would ask git for a patch that does not exist, so the page must only select among files that
  belong to the selected row.
- In tree mode past `AutoExpandLimit` the directories open collapsed; the first file's directories must
  be opened for its row to be visible.

## Decisions

| Decision | Rationale |
|---|---|
| "First file" is the first **file** row in display order — the list's first row; in the tree, the first file depth first | What the reader sees at the top is what "first" means |
| `ChangedFilesPanelViewModel.SelectFirstFile()` selects it, opening its collapsed ancestors, and returns whether anything was selected | One place for the rule, testable on its own |
| `SetFiles(files, keepSelection: true)` — the history passes `false`, the other hosts keep the default | A new commit is a new list; the other hosts refresh the same one |
| The history selects the first file **only while the diff view is open**: when it opens, and when files arrive while it is open | A plain row click loads no patch nobody is looking at |
| The page remembers which row its files belong to, and selects nothing on stale files | Never a patch of the previous commit's file against the new commit |

## Steps

1. `ViewModels/Panels/ChangedFilesPanelViewModel.cs` — `SetFiles(IReadOnlyList<ChangedFile>, bool
   keepSelection = true)`; `Rebuild` takes the path to keep; `SelectFirstFile()`.
2. `ViewModels/Pages/HistoryPageViewModel.cs` — the files are set with `keepSelection: false`, the row
   they belong to is remembered, and the first file is selected when the view opens and when files
   arrive while it is open.
3. Tests — `ChangedFilesPanelTests`: `SelectFirstFile` in the list and in a collapsed tree, on an empty
   panel, and `SetFiles(…, keepSelection: false)` dropping the previous path. `HistoryPageTests`:
   opening the diffs selects the first file; opening another commit whose files include the previously
   selected path still selects its first file; closing after picking another file and reopening selects
   the first again; selecting a row without opening its diffs selects no file.

## Acceptance criteria

- Opening the diffs of any commit with files shows its first file selected and its diff loaded.
- The previously selected file is never re-selected on another commit, nor on the same commit reopened.
- A row selected without opening its diffs has no file selected and loads no patch.
- The changes page and the stash panel behave as before.
- Build clean with zero warnings; the whole suite green, including the new tests.

## Out of scope

- The changes page and the stash panel's selection.
- Remembering a file per commit, or any preference for this.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| FEATURE or BUG | FEATURE | Keeping the previous path is today's designed behaviour; this changes the requirement | BUG |
| When the first file is selected | When the diff view opens, and when files arrive while it is open | Satisfies "never open without the first file selected" without loading patches for rows nobody opened | On every row selection (a patch read per click, for a hidden view) |
| Reopening the same commit's diffs | Selects the first file again | The draft: "never select the previous file that was selected before" | Keeping the file picked last time |
| Other hosts of the panel | Unchanged — `keepSelection` defaults to `true` | They refresh the same change in place; the draft is about a commit's diffs | Resetting them too |
| A collapsed tree | The first file's directories are expanded | A selected row the reader cannot see is no selection | Selecting inside a collapsed directory |
