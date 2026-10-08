# BUG-D0D9 — Diff messages shown on their own

**Status:** DONE — see `docs/done/BUG-D0D9.md`
**Type:** BUG
**Branch:** `bugfix/bug-d0d9-diff-message-alone`
**Run:** vibe/2026-10-08-file-panel-about-release

## Objective

When the diff viewer has a message to give instead of a patch, it shows that message alone, centred
and readable, with no empty editor drawn around it. The messages are:

- "Select a file to see what changed in it."
- "This file is binary, so there is no text to compare."
- a rename or copy with no content change;
- a mode change;
- a submodule;
- "This change touches no lines of text."
- "The diff could not be read."

## Context & constraints

- Reported on binary files, and sometimes after letting go of a binary file or a folder: the message
  appears "a bit hidden in the middle", behind the empty diff structure with its line-number gutter.
- In `DiffViewerView.axaml`, the `EmptyState` is the first child of a `Grid`. The unified grid and the
  side-by-side grid are drawn after it, over it, and are visible whenever their mode is chosen,
  patch or not.
- `DiffViewerViewModel.HasMessage` is true exactly when a message stands in for a patch (`Apply`
  sets the message with the rows).
- The minimaps already hide without a patch (`HasPatch`); the editors do not.
- The viewer is the history's diff page. The working tree uses the same viewer through the same page.

## Steps

1. `DiffViewerViewModel`:
   - `ShowsUnified` is `IsUnified && !HasMessage`;
   - `ShowsSideBySide` is `IsSideBySide && !HasMessage`;
   - both are announced wherever their inputs change: the view mode, and every `Apply`.
2. `DiffViewerView.axaml`: the unified and side-by-side grids bind `ShowsUnified` / `ShowsSideBySide`
   instead of `IsUnified` / `IsSideBySide`. The toolbar is unchanged.
3. Tests:
   - `DiffViewerTests`:
     - a binary file, a cleared viewer and a rename-only change show neither rendering;
     - a patch shows the chosen one;
     - switching the mode with a message shown keeps both hidden.
   - A headless view test: with a binary file, the `EmptyState` is the only thing visible in the
     content area. With a patch, the editor is visible and the `EmptyState` is not.

## Acceptance criteria

- Every message is shown alone, with no editor, gutter or minimap visible around it, in either mode.
- A patch with lines renders exactly as before.
- Build clean with zero warnings; the affected suites are green.

## Out of scope

- Rewording any message.
- Showing anything for binary files beyond the message, such as image previews.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to show the message alone | Hide both renderings while a message stands in for the patch | It is exactly what the report asks; one rule covers every message | Drawing the `EmptyState` on top of the editors (the empty gutter would still show around it) |
| Where the rule lives | Two view-model properties | Testable without a window, as the rest of the viewer is | A `MultiBinding` in XAML |
| The toolbar with a message | Unchanged | It still names the file; the buttons are disabled through their commands when there is no patch | Hiding the toolbar |
