# FEATURE-8E87-PHASE03 — Branches as a compact tree

**Item:** FEATURE-8E87 — Compact ref rows with multi-select
**Phase:** PHASE03 — Branches as a compact tree
**Branch:** `feature/feature-8e87-phase03-branch-tree`
**Run:** feature/2026-10-08-title-watcher-refs-release

## Summary

The branches page is now a tree, and its lines are drawn like the changed files'.

- **The tree** (`ViewModels/Pages/BranchTree.cs`):
  - Three kinds of node, all `BranchTreeNode`s:
    - a top-level node (`BranchGroupViewModel`): *Local*, or one remote;
    - a folder (`BranchFolderViewModel`);
    - the branch's line, `BranchRowViewModel`, as the leaf.
  - Each node has a key that survives a rebuild, a label, its children and `IsExpanded`.
  - `BranchTreeBuilder.Build` is pure:
    - *Local* first, then one node per remote, alphabetical;
    - every `/` segment of a name is a folder;
    - a chain of folders that each hold only one folder is one line (`vibe/2026-10-08`), by the
      same rule as `FileTreeBuilder`;
    - folders come first, by name, then the branches in the page's order and direction;
    - a node with no branch under it is not there.
- **What is open:**
  - A top-level node starts open and a folder closed.
  - The page remembers what the reader opens and closes, by node key, across every rebuild. It
    forgets it with the repository.
  - While the filter narrows the tree, every node is open, so each match shows with the folders
    above it. What the reader opens or closes during a filter is not remembered, and clearing the
    filter brings back the tree as they left it.
- **A branch's line:**
  - the shared 22 px `listrow` with a dim `GitBranch` icon, and the last segment of the name (the
    folders above it say the rest);
  - small badges on the right, each only when it applies, in this order: *checked out*, *hidden*
    with `EyeSlash`, `↑n`, `↓n`, *local only*;
  - each badge has a tooltip and an automation name saying what it means;
  - no bold, no dimming, no *upstream gone* or *published* marker;
  - the line's tooltip gives:
    - the full name;
    - for a local branch, what it tracks (or that it tracks nothing);
    - the last commit's subject;
    - the author and the date.
- **Lines of top-level nodes and folders** behave as the changed files' folders do (BUG-1B14):
  - a click folds them and never selects them, and the branch selected before stays selected;
  - the chevron folds as before;
  - a double-click folds once;
  - the arrow keys can reach a folder, but the page refuses it and the tree is put back on the
    branch.
- **Unchanged:**
  - Drag-to-merge works on the tree's lines, and only branch lines take a drop.
  - The line's menu keeps every item, *Hide from the history* among them, and the whole line
    answers a right-click.
  - The selection is restored by name.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchTree.cs`: `BranchTreeNode`,
  `BranchFolderViewModel`, `BranchGroupViewModel`, `BranchTreeBuilder`.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchTreeBuilderTests.cs`: 11 tests.
  - The top level and its keys; an empty node left out.
  - Folders, merged chains, and a remote's folders.
  - Folders first whichever way the branches run; the branches in date order.
  - Default and remembered expansion; everything open while filtering.
  - Expansion changes reported for the reader's changes only, not while building.
- `docs/done/FEATURE-8E87-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`:
  - `BranchRowViewModel` is the tree's leaf, with `Label`, `ToolTip` and the badges' tips;
  - `IsLocalOnly` now covers a branch whose upstream has gone;
  - the page builds the tree, remembers expansion and refuses a folder as the selection;
  - the flat `Items`, `IBranchListItem` and the heading line are gone.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml`: the `TreeView` with its three
  templates, the badges, and the expansion binding.
- `src/Enigma.GitClient.Desktop/Views/Pages/BranchesPageView.axaml.cs`:
  - folder lines fold on a click, fold once on a double-click, and give the selection back after
    the arrow keys;
  - the drag finds its rows in the tree, and only branch lines take a drop.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`:
  - the page and drag tests moved to the tree;
  - new tests:
    - the tree's top level;
    - expansion surviving a refresh, in the view model and on screen;
    - folders never selected, by the page, by a click and by the keyboard;
    - the line's text and badges, and every badge's tooltip;
    - a branch whose upstream has gone is *local only*.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchVisibilityTests.cs`: a hidden branch's line says
  *hidden*, and its menu shows it again.
- `tests/Enigma.GitClient.Desktop.UnitTests/ToolDialogTests.cs`,
  `tests/Enigma.GitClient.Desktop.UnitTests/WholeLineMenuTests.cs`: the branch tree in place of the list.
- `README.md`: hiding a branch is done from its line's menu (see the sweep).
- `docs/roadmap.md`, `docs/plan/FEATURE-8E87.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The page's collection | Kept the existing name `Groups`, now the top-level nodes, instead of the plan's `Roots` | The drag code and the tests already read `Groups`; the name is still true |
| Node keys | `local`, `remote:<name>`, then `<top-level key>/<folder path>`; a branch is `branch:<full ref>` | Stable across rebuilds and unique; a remote called "local" cannot collide with the local branches |
| Restoring expansion without echoing it | `Restore(expanded, callback)` sets the state first and wires the callback after | Building the tree must not write into the memory it reads from |
| What a filter does to the memory | Everything opens; the reader's folds during a filter are not recorded | The filter's open nodes are the filter's, not the reader's; clearing it brings back their tree |
| A branch whose upstream has gone | *local only* | The *upstream gone* marker is gone (plan step 3), and the branch is on no remote |
| A folder holding a single branch | Stays a folder; only folder chains merge | The changed-files tree does the same (`FileTreeBuilder`) |
| The top-level icons | `HardDrives` for a remote, `Desktop` for *Local*, dim | A remote's mark is the one the old headings and the remotes page's lines use; *Local* had none, and every line has a mark in its first column |
| Clicks on folder lines | Handled on the tunnelling press, the chevron (a button) left to the tree | The BUG-1B14 pattern: the tree never sees the press, so it cannot move the selection |

## Deviations & follow-ups

- **`Roots` → `Groups`:** see the decisions.
- **Line height:** a line is the shared 22 px `listrow`. The tree's items keep their theme spacing,
  32 px from line to line, as the changed-files tree does: the two trees are drawn the same.
- I looked at the rendered snapshot (`branches-page.png`). It shows *Local* with *main*
  (*checked out*), *merged* and *unmerged* (*local only*), and *origin* with its two branches.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

- `README.md`: "Hide a branch from the history with the eye on its row in the branches dialog" was
  made wrong by this phase, which takes the eye off the row. It now names *Hide from the history* in
  the line's menu and the *hidden* badge.
- `docs/RELEASE.md`'s smoke check ("a branch hidden in the branches dialog stays hidden") is still
  true.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx -c Debug`: 3015 total, 3014 passed, 1 skipped,
  0 failed.
- Fix budget: 0 cycles. During development, before the first full run:
  - the page's tests expected *local only* neither on `main` nor on a branch whose upstream had gone;
  - they were updated to the behaviour the plan asks for.
