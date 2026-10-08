# BUG-1B14 — Folders in the files panel never select

**Item:** BUG-1B14 — Folders in the files panel never select
**Branch:** `bugfix/bug-1b14-folders-never-selected`
**Run:** vibe/2026-10-08-file-panel-about-release

## Summary

A folder line in the files panel is never selected any more. The panel is the one beside the
history, for a commit's files and for both halves of the working tree.

- **A click on a folder folds or unfolds it.** The file selected before keeps the selection, and its
  diff stays open. With nothing selected, the graph stays.
- **A double-click folds it once.**
- **The arrows can no longer select one.** The keyboard focus may rest on a folder, but the
  selection, and the diff, stay on the file.
- **The right-click menu still opens on a folder.**

**One click on a file now always selects it.** The lost first click had a root cause beyond folders,
confirmed by experiment:

- The panel's view holds a `ListBox` and a `TreeView`, one of them hidden, and both had a two-way
  binding to `SelectedNode`.
- In tree mode, the hidden `ListBox` holds only the top-level rows.
- When a nested file was picked while a top-level row (a folder, or a root file) was selected, the
  `ListBox` could not select the file. It dropped to `null` and wrote that `null` back over the tree's
  choice.
- With the old binding put back, the new test `ARootFileThenANestedFile_…` fails with
  `Expected: guide.md, Actual: null`. With the fix, it passes.

**How:**

- `ChangedFilesPanelViewModel.SelectedNode` refuses a directory row from any source: the list, the
  keyboard or code. The selection stays, no `SelectionChanged` is raised, and the selection is
  announced again (`AnnounceSelection`).
- `ListSelection` / `TreeSelection` are mode-gated projections of `SelectedNode`. Each control is
  handed the selection only while it is the one shown, and is not listened to otherwise. The view
  binds them instead of `SelectedNode`. A view-mode switch announces them after the rebuild, so the
  control now shown is handed a row it holds.
- `ChangedFilesPanelView`:
  - a left press on a folder line (outside its chevron and buttons) folds or unfolds it on the first
    press and is handled, so the tree never sees it;
  - the tree item's own double-tap fold is taken back (`OnDoubleTapped`), so a double-click folds
    once;
  - when the keyboard makes the tree select a folder, the tree is put back on the panel's selection
    once it has finished selecting (`OnTreeSelectionChanged`). The view model's re-announcement
    arrives while the tree is still mid-selection, and the tree ignores it.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml`
- `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/ChangedFilesPanelTests.cs`:
  - `Panel_SelectingADirectoryRowSelectsNoFile` is replaced by
    `Panel_NeverSelectsADirectoryRow_AndKeepsTheFileItHad`;
  - `Panel_HandsTheSelectionOnlyToTheControlShown_AndListensOnlyToIt` is new.
- `tests/Enigma.GitClient.Desktop.UnitTests/FileListToggleTests.cs`: the tree test is now
  `InTheTree_TheSelectedFileLetsGo_AndAFolderIsNeverSelected`. It covers a folder click with nothing
  selected, a folder click with a file's diff open, and the next file one click away.
- `docs/roadmap.md`, `docs/plan/BUG-1B14.md`: statuses.

**Created**

- `tests/Enigma.GitClient.Desktop.UnitTests/FolderLineTests.cs`: six repository-free headless tests of
  the panel. They cover a click, a click with a file selected, a root then a nested file, a
  double-click, the keyboard, and a right-click.
- `docs/done/BUG-1B14.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Bringing the tree back after a refused folder | Posted `SetCurrentValue` on the tree's `SelectedItem` from its `SelectionChanged` | Measured: the view model's synchronous re-announcement is ignored while the tree is mid-selection. `SetCurrentValue` keeps the two-way binding |
| A double-click on a folder | The press folds; the tree's own double-tap fold is undone to what the press left | Measured: the tree item folds on a double-tap even when the presses are handled. Restoring the press's state, rather than toggling again, holds whatever the tree does |
| A folder press with modifiers (Shift, Ctrl) | Folds it, like a plain click | A folder has no selection to extend |
| Keyboard focus on a folder | Left there; only the selection is refused | The next arrow goes on from the focused line, so arrowing through a folder never gets stuck |

## Deviations & follow-ups

- **Deviation: the Desktop suite is not run.** Mid-dev, you said not to run the Desktop tests this
  session, because they are broken on Windows. The plan's "affected suites are green" therefore
  covers the Core suites only. The Desktop tests are written and compiled, and the six `FolderLineTests`
  ran green before your instruction.
- **A temporary, never-committed teardown, now gone.** Before your instruction, a full Desktop run was
  started with a temporary change to `TestServices.Dispose`, which cleared git's read-only attribute
  first (BUG-6EAA). That run was stopped when you asked, and no result is claimed from it. The change
  was reverted by hand: `TestServices.cs` is unchanged and not in this commit.
- **Run before tagging, on Linux:** the Desktop suite, in particular `FolderLineTests`,
  `ChangedFilesPanelTests` and `FileListToggleTests`.
- **Worth a click before release:**
  - a commit touching a root file and files in folders: pick the root file, then a nested one;
  - click and double-click folders;
  - arrow through the tree.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing stale. `README.md` and `docs/RELEASE.md` do not describe the files panel's selection. The
release notes of earlier versions are history and are left as they are. No `CLAUDE.md` or `AGENTS.md`
exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1165 total, 0 failed, 1 skipped.
  `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` was excluded, because it hangs on
  Windows (BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 0 failed, 2 skipped.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your instruction. The exception is
  `FolderLineTests`, which ran before it: 6/6 passed. These tests touch no repository, so their teardown
  is not affected.
- Root cause confirmed by experiment: with the view bound to `SelectedNode` again, the root-then-nested
  test fails with `Expected: guide.md, Actual: null`. The binding was restored after the experiment.
- Fix budget: 0 cycles. The two `FolderLineTests` failures during implementation (the keyboard, the
  double-click) came before the first full-suite run, and shaped the two view handlers.
