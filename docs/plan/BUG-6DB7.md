# BUG-6DB7 — A refresh reopens collapsed folders

**Status:** DONE — see `docs/done/BUG-6DB7.md`
**Type:** BUG
**Branch:** `bugfix/bug-6db7-tree-keeps-collapsed-folders`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

When the automatic refresh (every 15 s by default) re-reads the working tree, the uncommitted files'
tree keeps the folders the user collapsed, instead of being rebuilt with every folder open again.

## Context & constraints

- `ViewModels/Panels/WorkingTreePanelViewModel.cs`: `StateRefreshed` → `RefreshAsync` → `Apply` →
  `Unstaged.SetFiles(...)` / `Staged.SetFiles(...)` on every refresh while the panel is shown.
- `ViewModels/Panels/ChangedFilesPanelViewModel.cs`: `SetFiles` → `Rebuild` clears `Nodes` and builds
  new `ChangedFileNodeViewModel`s, every directory at `expand = visible.Count <= AutoExpandLimit`. The
  view binds `IsExpanded` two-way (`ChangedFilesPanelView.axaml`), so a collapse is in the old node and
  lost with it. The selection is already kept (`SelectPath`).
- `SetFiles(files, keepSelection: false)` is "another change altogether" (another commit).
- `ChangedFile` is a record (value equality).
- Tests: `ChangedFilesPanelTests`, `WorkingTreePanelTests`, `FileListToggleTests`.

## Steps

1. `ChangedFilesPanelViewModel.Rebuild` remembers each directory row's `IsExpanded` by its path before
   clearing, and a rebuilt directory with the same path takes it back; a directory not seen before gets
   the default. This holds for a refresh of the same change, the filter and the list/tree toggle.
2. `SetFiles(..., keepSelection: false)` — another change — starts from the defaults.
3. `SetFiles` with the same files as already shown, for the same change, rebuilds nothing.
4. Tests: a collapsed folder stays collapsed across a refresh that changes another file, and across one
   that changes nothing; an expanded folder past the auto-expand limit stays expanded; another change
   starts from the defaults; the working-tree panel keeps a collapsed folder across `StateRefreshed`.

## Acceptance criteria

- Collapsing a folder in the uncommitted files survives the automatic refresh.
- A commit's files, opened anew, start from the default expansion.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- Remembering expansion across commits or across sessions.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the state lives | In the panel's rebuild, keyed by directory path | Fixes every caller of the shared panel at once; paths are stable across refreshes | A separate store in the working-tree panel |
| Unchanged files | No rebuild at all | No flicker, no scroll jump, nothing to restore | Rebuilding and restoring |
| Another change | Defaults | A new commit's files are a new view | Carrying collapses across commits |
