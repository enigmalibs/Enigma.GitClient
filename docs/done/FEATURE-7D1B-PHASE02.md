# FEATURE-7D1B-PHASE02 — Colour-coded diff viewer

**Item:** FEATURE-7D1B — Commit details & diff viewer
**Branch:** `feature/feature-7d1b-phase02-diff-viewer`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The second of the two headline features is delivered. Selecting a file in the changed-files panel
now shows its patch: **unified or side by side**, with added, removed and context lines tinted
apart, the stretches a word-level diff found tinted more strongly inside them, old and new line
numbers in a gutter, and git's `@@` band introducing each hunk. Clicking that band asks git for more
of the file around the change; the toolbar carries the shape toggle, whitespace handling, tab and
wrap options, and the two copy actions. A binary file, a submodule, a pure rename and a pure mode
change each say what happened instead of showing an empty pane, and a file past the size limit
offers to be rendered anyway.

With this phase FEATURE-7D1B is complete.

## Files / modules touched

**Created — Core**

- `Diff/DiffRows.cs` — `DiffRowKind`, `DiffRow`, `DiffPairRow` and `DiffRowBuilder`: the projection
  from a parsed patch onto the flat row lists both shapes render, plus the copy helpers

**Created — App**

- `Controls/Diff/DiffLineText.cs` — draws one line: tab expansion to a fixed column grid, optional
  whitespace symbols, and the word-level tint behind the changed stretches
- `ViewModels/Panels/DiffViewerViewModel.cs` — `DiffViewMode`, `DiffRenderOptions`,
  `DiffCellViewModel`, `DiffRowViewModel` and the viewer itself
- `Views/Panels/DiffViewerView.axaml` (+ code-behind) — the toolbar, the two row templates, the
  truncation band and the message pane

**Modified**

- `src/Enigma.GitClient.App/Themes/Graph.axaml` — thirteen diff-viewer colours per variant
- `src/Enigma.GitClient.App/Themes/Styles.axaml` — the row, gutter, marker, band and selection styles
- `src/Enigma.GitClient.App/Views/Panels/ChangedFilesPanelView.axaml` — a selected row's dimmed
  columns now use the full foreground, and the tree has room on the right
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — holds the viewer and hands
  it the file the panel selected
- `src/Enigma.GitClient.App/Views/Pages/HistoryPageView.axaml` — the viewer replaces the placeholder
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs`
- `tests/.../Infrastructure/` — a recording diff service, and the snapshot colour counter extracted
  so three test classes share it
- `docs/roadmap.md`, `docs/plan/FEATURE-7D1B.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Diff/DiffRowBuilderTests.cs` — 16 cases on both
  projections, the alignment and the copy helpers
- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — 40 cases: the shapes, everything the
  viewer asks git for, the stand-in messages, truncation, virtualisation, copying, tab expansion,
  the theme contrast check, four rendered snapshots and one end-to-end run against a real repository

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the row projection lives | `Core`, beside the model | It is pure list arithmetic with no UI in it, the alignment is the part most worth testing, and both shapes have to agree about line numbering |
| How the two side-by-side columns stay synchronised | One list of paired rows, not two scroll viewers | A single viewer cannot drift from itself. Two viewers means writing and maintaining scroll synchronisation on both axes, and it breaks the first time one side virtualises differently from the other |
| Unbalanced edits | Filler rows padding the shorter run | Without them, one edit that replaces two lines with three shifts every later line on one side and the view stops being readable |
| How a line is drawn | A `Control` with one `FormattedText`, not a `TextBlock` with `Inlines` | Inlines would re-create an inline collection per row and give no control over tab expansion or whitespace rendering. One `FormattedText` draws the same thing in a single pass and reports its own width |
| Tab expansion | Done by the control, to a fixed column grid | A tab's width depends on what precedes it, so two lines differing only after a tab would not line up. Fixed columns is what makes indented code readable |
| Expanding context | Re-runs git with a larger `-U` | Context is git's to produce; the viewer has nothing beyond what it was given. The affordance is the hunk band itself, which is already where the reader is looking |
| Which options re-read | Whitespace handling and context do; show-whitespace, wrap and tab width do not | The first two change what git produces, the last three only change how it is drawn. Tests pin both halves so the distinction cannot quietly erode |
| Copying | Selection copies plain text, the toolbar copies the patch | Copying a diff is nearly always copying code, and code pasted with `+` on it does not compile. The whole patch, markers included, is the other thing people want and is its own command |
| A context line in side-by-side | Yielded once when copied | It is the same line object on both sides; yielding it twice would double every unchanged line of a selection |
| The selected row's look | Its own tint kept, a bar drawn in the gutter | Painting the selection over the row would hide whether the line was added or removed — backwards for a view whose selection exists to be copied from |
| The word tints | Tuned against a measured contrast test | Chosen by running the numbers, not by eye: the first pass failed its own 4.5:1 check in the dark variant at 3.44:1, and the line-to-word separation needed widening to 1.8:1 before the highlight was actually visible |

## Deviations & follow-ups

- **Deviation:** the side-by-side rendering shares one horizontal scrollbar rather than giving each
  column its own. Two independent horizontal scrollers are what the plan's wording implies, but one
  shared viewer is what makes the two columns provably synchronised; independent horizontal panning
  is recorded as a refinement.
- **Deviation:** the combined diff of a merge (`IsCombined`) is modelled and parsed but has no
  dedicated rendering — it falls back to the ordinary one, and the merge-parent switch is still
  deferred. Both belong with FEATURE-6DCC, which is where a merge is something the user is doing
  rather than something they are reading.
- **Deviation:** the viewer's options are per-session; nothing is persisted. The settings store is
  FEATURE-5D77's.
- **Follow-up:** hunk-level and line-level staging (the obvious next thing to do from a selection) is
  FEATURE-13FE's, and was already recorded as out of scope by the plan.
- **Follow-up:** syntax highlighting is not attempted. It needs a grammar set and a tokeniser, and it
  is a separate piece of work from showing what changed.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 795  failed: 0  succeeded: 795  skipped: 0
```

57 tests are new in this dev. The Core projection passed on its first run. Two defects were found by
measuring rather than by looking — the dark word tint failed its own contrast check at 3.44:1, and
the line-to-word separation was too small for the highlight to register — and two more by looking at
the rendered frames: a selected file row's dimmed columns went unreadable on the selection colour,
and the selection painted over the diff tint it exists to point at. The frames are written to
`snapshots/diff-unified.png`, `diff-unified-selected.png`, `diff-side-by-side.png`, `diff-binary.png`
and, for the whole feature end to end, `history-page-diff.png`.
