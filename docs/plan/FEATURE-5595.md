# FEATURE-5595 — Branch tree open, expand/collapse all

**Status:** DONE — see `docs/done/FEATURE-5595.md`
**Type:** FEATURE
**Branch:** `feature/feature-5595-branch-tree-expand`
**Run:** feature/2026-10-21-ui-polish-release-650

## Objective

The branches page's tree opens fully by default. Its header gains **Expand all** and **Collapse all**
buttons, as the changed-files panel has (FEATURE-E2D2).

## Context & constraints

- `BranchTreeBuilder.Build` opens a top-level node (*Local*, each remote) by default and leaves a
  folder closed, unless the page remembered otherwise. While the filter narrows the tree, everything
  is open (`expandAll`).
- `BranchesPageViewModel` remembers what the reader opened or closed (`_expanded`, by node key):
  - not while filtering;
  - and it forgets on a repository change.
- A folder or a top-level node is never part of the selection, so closing one cannot move the
  selection onto it.
- The header grid holds: title, filter box, sort, *Delete*, *New branch*.
- FEATURE-E2D2's buttons: `toolbar` class, `CaretDoubleDown` / `CaretDoubleUp`, tooltips and automation
  names "Expand all folders" / "Collapse all folders", named `ExpandAll` / `CollapseAll`.
- Desktop tests are written and compiled, **not run** on this machine.

## Steps

1. `BranchTreeBuilder`: a folder with no remembered state starts open, as a top-level node does.
   Update the remarks.
2. `BranchesPageViewModel`:
   - `ExpandAllCommand` and `CollapseAllCommand` set `IsExpanded` on every folder node, top-level
     nodes included, nested at any depth;
   - both can run when the tree shows at least one node, and are re-evaluated on every rebuild;
   - what they set is remembered through the existing expansion callback, as a click would be.
3. `BranchesPageView.axaml`: the two `toolbar` buttons in the header, before the sort controls, with
   E2D2's icons, tooltips, automation names and button names.
4. Tests:
   - `BranchTreeBuilderTests`: a folder starts open;
   - `BranchesPageTests`: expand all / collapse all reach nested folders; collapse closes the
     top-level nodes; the state survives a refresh; the selected branch stays selected through a
     collapse; the commands are disabled on an empty tree;
   - adjust the existing tests that assume a folder starts closed.

## Acceptance criteria

- Opening the branches page shows every branch: every node and folder open.
- *Expand all* opens every node; *Collapse all* closes every node, the top-level ones included.
- The reader's open/closed choices, including those made by the buttons, survive a refresh.
- The selected branches stay selected through either button.
- The build is clean with zero warnings. The Core suites are green. The Desktop suite compiles.

## Out of scope

- The same buttons on the tags or remotes pages (lists, not trees).
- Persisting the expansion state across sessions or repositories.
- A keyboard shortcut.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| "Expand all by default" | Every folder starts open unless the reader closed it | What you asked; remembered choices still win, so a refresh does not undo the reader's clicks | Forcing everything open on every rebuild |
| Does *Collapse all* close the top-level nodes | Yes | A collapse-all that leaves *Local* and every remote open still shows hundreds of lines | Folders only |
| Placement | In the header, before the sort controls | Next to the other controls that change how the tree is shown | A row above the tree |
| Look | E2D2's icons, tooltips, names | One vocabulary across the application's trees | New icons |
| Enabled when | The tree shows any node | Nothing to open or close otherwise | Always enabled |
