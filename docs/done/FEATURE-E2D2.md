# FEATURE-E2D2 — Expand all / collapse all in the tree

**Item:** FEATURE-E2D2 — Expand all / collapse all in the tree
**Branch:** `feature/feature-e2d2-expand-collapse-all`
**Run:** vibe/2026-10-08-file-panel-about-release

## Summary

While a files panel shows a tree, its header has two more buttons, before the list/tree toggles and
set apart from them by a thin divider:

- **Expand all folders** (`CaretDoubleDown`) opens every folder, nested ones included, whatever the
  auto-expand limit.
- **Collapse all folders** (`CaretDoubleUp`) closes every folder.

**When they are there:**

- In list mode, both buttons and the divider are hidden.
- In a tree with no folder (only root files, or nothing shown), the buttons are disabled.
- Every files panel gets them: a commit's files, and both working-tree halves.

**What they keep:**

- The selected file stays selected through either, so its diff stays open. After BUG-1B14, a folder
  can never take the selection.
- The open or closed state survives a refresh of the same change, through the existing
  `RememberExpansion`.

**How:** `ChangedFilesPanelViewModel` gains `ExpandAllCommand` and `CollapseAllCommand`:

- they set `IsExpanded` on every directory row;
- they can run only in tree mode with a folder among the top rows;
- they are re-evaluated on every rebuild, which covers a view-mode change, a refresh and the filter;
- they are created before the constructor applies the preferences, because applying them rebuilds the
  rows.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Panels/ChangedFilesPanelView.axaml`
- `tests/Enigma.GitClient.Desktop.UnitTests/ChangedFilesPanelTests.cs`, five tests:
  - every folder collapsed, then expanded, nested ones included;
  - expanded past the auto-expand limit;
  - offered only in a tree with folders;
  - kept across a refresh;
  - the selected file kept through *Collapse all*.
- `tests/Enigma.GitClient.Desktop.UnitTests/FolderLineTests.cs`: a headless test of the header. The
  buttons are visible in the tree only, they close and open every folder, and the selected file stays.
- `docs/roadmap.md`, `docs/plan/FEATURE-E2D2.md`: statuses.

**Created**

- `docs/done/FEATURE-E2D2.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Separating the new buttons from the toggles | A 1 px divider, hidden with them in list mode | The diff toolbar's own way of grouping buttons |
| "Has a folder" | Any directory among the top rows | A nested folder always sits under a top one; no walk of the whole tree on every rebuild |
| Button names | `ExpandAll`, `CollapseAll` | For tests, as other named toolbar buttons are |

## Deviations & follow-ups

- **The Desktop suite is not run** (your instruction for this session). The new tests are written and
  compiled.
- **Worth a click before release:** select a file deep in a folder, then *Collapse all*.
  - The view model keeps the file selected, and a folder can never take the selection.
  - Whether the tree control itself shows the selection as dropped when the selected row's folder
    closes is not verified without running the Desktop tests.
  - The headless test covers this: `TheTreesHeader_ExpandsAndCollapsesEveryFolder_AndTheFileStaysSelected`.
- **On a commit with thousands of files**, *Expand all* realises every row: that is what the
  auto-expand limit avoids on its own. It is an explicit request, so it is not capped.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing stale. `README.md` does not list the files panel's header buttons. No `CLAUDE.md` or
`AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1165 total, 0 failed, 1 skipped (the AtomicFile race excluded on
  Windows, BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 0 failed, 2 skipped.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your instruction.
- Fix budget: 0 cycles.
