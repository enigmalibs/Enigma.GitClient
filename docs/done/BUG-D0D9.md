# BUG-D0D9 — Diff messages shown on their own

**Item:** BUG-D0D9 — Diff messages shown on their own
**Branch:** `bugfix/bug-d0d9-diff-message-alone`
**Run:** vibe/2026-10-08-file-panel-about-release

## Summary

When the diff viewer has a message instead of a patch, it now shows the message alone, centred, with
no editor, gutter or minimap around it. The messages are:

- "Select a file…";
- a binary file;
- a submodule;
- a rename, copy or mode change with no content change;
- no lines of text;
- "The diff could not be read."

**Cause:** in `DiffViewerView`, the `EmptyState` is the first child of the content `Grid`. The unified
grid and the side-by-side grid came after it, drawn over it. They were visible whenever their mode was
chosen, patch or no patch. Their empty editors (background, line-number gutter) covered the message,
which read as "a bit hidden in the middle".

**Fix:**

- `DiffViewerViewModel` gains `ShowsUnified` (`IsUnified && !HasMessage`) and `ShowsSideBySide`
  (`IsSideBySide && !HasMessage`). Both are announced on every `Apply` (a patch or a message drawn) and
  on a view-mode change.
- The view's two rendering grids bind them instead of `IsUnified` / `IsSideBySide`.
- The toolbar is unchanged.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Panels/DiffViewerViewModel.cs`
- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml`
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffViewerTests.cs`, three new tests:
  - `Viewer_DrawsNoRenderingAroundAMessage_InEitherShape`;
  - `Viewer_DrawsTheChosenRenderingOfAPatch_AndNoneOnceCleared`;
  - `Viewer_DrawsAMessageAlone_AndAPatchWithoutIt` (headless view: the message alone, then the editor
    alone).
- `docs/roadmap.md`, `docs/plan/BUG-D0D9.md`: statuses.

**Created**

- `docs/done/BUG-D0D9.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The first change still scrolled into view when an editor goes from hidden to shown | Unchanged code | `ShowFirstChange` is posted at background priority and lays the editor out first, so it runs after the grid is shown and measured |
| Existing diff tests that find the editors by name | Unchanged | Each loads its patch before the view is created, so its editors are shown, as before |

## Deviations & follow-ups

- **The Desktop suite is not run** (your instruction for this session). The new tests are written and
  compiled. Run them on Linux before tagging.
- **Worth a click before release:**
  - pick a binary file, in both shapes;
  - let go of a file;
  - pick a rename-only change.
  In each case the message alone is centred in the diff area.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing stale. `README.md` and `docs/RELEASE.md` do not describe the diff viewer's empty states. No
`CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1165 total, 0 failed, 1 skipped (the AtomicFile race excluded on
  Windows, BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 0 failed, 2 skipped.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, at your instruction.
- Fix budget: 0 cycles.
