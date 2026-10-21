# FEATURE-5595 — Branch tree open, expand/collapse all

**Item:** FEATURE-5595 — Branch tree open, expand/collapse all
**Branch:** `feature/feature-5595-branch-tree-expand`
**Run:** feature/2026-10-21-ui-polish-release-650

## Summary

The branches page now opens on every branch, and its header gains two buttons.

**Open by default.** `BranchTreeBuilder` opens a folder that has no remembered state, as it already
did for a top-level node. A node the reader closed stays closed across refreshes, because the page
remembers expansion by key.

**The buttons** sit in the header, before the sort controls. They use the changed-files panel's look
(FEATURE-E2D2):

- **Expand all folders** (`CaretDoubleDown`) opens every node: the top-level ones and every folder,
  nested ones included.
- **Collapse all folders** (`CaretDoubleUp`) closes every node, the top-level ones included.

**How the commands behave:**

- `ExpandAllCommand` and `CollapseAllCommand` set `IsExpanded` on each node. That goes through the
  existing expansion callback, so the result is remembered exactly as a click would be (and, as
  before, not while the filter is open).
- They are enabled whenever the tree shows a node, and re-evaluated on every rebuild.
- The selection is untouched: a node is never part of it, and `RestoreSelection` works by key over
  every row, shown or not.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchTree.cs`: a folder starts open; the remarks
  are updated.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`: the two commands,
  `SetAllExpanded`, `Folders`, and the can-execute refresh in `Rebuild`.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`: a header column with the two
  buttons (`ExpandAll`, `CollapseAll`); the later columns shift by one.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchTreeBuilderTests.cs`:
  - `ATopLevelNodeStartsOpen_AndAFolderClosed` → `EveryNodeStartsOpen_ATopLevelNodeAndAFolderAlike`
    (a nested folder too);
  - the remembered-state and reported-change tests now close a folder, since opening an open one
    changes nothing.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`:
  - adapted:
    - the remembered-state test: closing a folder is remembered; closing a node while filtering is
      not;
    - `TheTree_KeepsAFolderTheReaderOpenedOpen_AcrossARefresh` →
      `…ReaderClosedClosed…`;
    - the click-to-fold test, which now starts open;
    - Ctrl+A, the arrows and the keyboard tests, which first close the folder so it still sits
      where those tests need it;
  - added:
    - `ExpandAllAndCollapseAll_ReachEveryNode_NestedFoldersAndTheTopLevelNodesToo`;
    - `CollapseAll_IsRememberedAcrossARefresh_AndKeepsTheSelectedBranches`;
    - `ExpandAllAndCollapseAll_HaveNothingToDo_WithNoRepository`;
    - `TheHeadersButtons_CollapseAndExpandTheTree` (a headless click on each button);
  - helper: `FoldersOf`.
- `docs/roadmap.md`, `docs/plan/FEATURE-5595.md`: statuses.

**Created**

- `docs/done/FEATURE-5595.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Can-execute | `Groups.Count > 0` | Every top-level node is itself a node to open or close; no tree walk on each rebuild |
| Order of the walk | Outermost first | A parent closes before its children; nothing depends on it, but it is the order a reader would click |
| Tooltip wording | "Expand all folders" / "Collapse all folders", as E2D2 | One vocabulary for both trees; *Local* and each remote read as folders in this tree |
| Tests that need a closed folder | Close it explicitly at the start | They test keyboard and Ctrl+A behaviour around a closed folder, not the default |

## Deviations & follow-ups

- **The Desktop suite is not run** on this machine (standing instruction). The new and adapted tests
  are written and compiled.
- Worth a click before release: select a branch deep in a folder, then *Collapse all*. The view model
  keeps it selected; whether the tree control keeps showing that selection after its container
  collapses is not verified without running the Desktop suite. The existing
  `AClickOnAFolder_FoldsIt…` test covers the top-level case.
- **On a repository with hundreds of branches**, the tree now creates every line when it opens, where
  closed folders used to hide most of them. Not measured. If it is noticeably slower, *Collapse all*
  is one click and is remembered for the session.
- Line endings: the touched files are LF; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: 388 total, 386 passed, 0 failed, 2 skipped (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction). Four tests
  added; nine adapted.

## Documentation sweep

Nothing stale. The README's branches line ("the branches as a tree — *Local*, then each remote, a
folder for every `/`…") does not say how the tree opens. The 6.5.0 notes (FEATURE-6E21) will announce
the buttons and the new default.
