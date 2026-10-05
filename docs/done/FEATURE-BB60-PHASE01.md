# FEATURE-BB60-PHASE01 — The diff editor control

**Item:** FEATURE-BB60 — Diffs on AvaloniaEdit
**Branch:** `feature/feature-bb60-phase01-diff-editor`
**Run:** feature/2026-10-05-diff-view-avaloniaedit

## Summary

A read-only AvaloniaEdit editor that draws one pane of a patch: `DiffTextEditor`. Nothing in the
viewer uses it yet — PHASE02 and PHASE03 put it there.

- **The document.** `DiffDocument.Build(rows, pane)` turns a rendering's rows into one document line
  per row:
  - a hunk band and a filler are empty lines;
  - each line records its kind, its old and new number, its marker, its word segments and, for a
    band, its header.

  A lone `\r` is shown as `␍`, one character for one, so the word diff's offsets still name the right
  characters and no row becomes two lines.
- **The gutter.** `DiffGutterMargin` is the editor's first left margin.
  - It numbers the unified pane with the old and the new number, and each side-by-side pane with its
    own side.
  - It is sized from `DiffGutterWidth` / `DiffMarkerWidth` and draws the marker in the added and
    removed colours over the row's tint.
  - A margin is outside the text's scrolling, so the numbers never move sideways.
- **The background.** `DiffBackgroundRenderer`, on the background layer, paints:
  - the added, removed and filler tints across the whole width;
  - each band, with its header at a fixed inset;
  - each changed word, located by `BackgroundGeometryBuilder` from the document offsets.

  Fills are aliased, so tinted rows meet without a seam.
- **The editor.**
  - It is read-only: no hyperlinks, no drag and drop, no current-line highlight, no scrolling below
    the document, no undo.
  - Whitespace symbols (`·`, `→`) and the tab width go through `TextEditorOptions`.
  - Every brush and gutter metric is a styled property, set by the `TextEditor.diff` style through
    `DynamicResource`, so the theme and the font preference reach it live.
- **Packages.** `Avalonia.AvaloniaEdit` 12.0.0 and `AvaloniaEdit.TextMate` 12.0.0 are pinned in their
  own CPM group. The Desktop project references the editor only; TextMate waits for PHASE04. AvaloniaEdit's
  Fluent theme is included after `FluentTheme`.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffDocument.cs` — `DiffDocumentLineKind`,
  `DiffDocumentLine`, `DiffDocument`.
- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffGutterMargin.cs`
- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffBackgroundRenderer.cs`
- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffTextEditor.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffTextEditorTests.cs` — 12 tests:
  - the document has one line per row with empty bands, and fillers keep the two sides level;
  - `\r` is shown one for one, and no rows gives an empty document;
  - the gutter numbers each pane with its own side and is two columns unified, one side by side;
  - the editor rebuilds for its rows and pane, is read-only, and passes whitespace and tab width to
    its options;
  - it takes every colour and metric from both themes;
  - in a rendered frame, the band, the removed and added tints, the word tint, the gutter colour and
    the marker column's tint are each where they belong.
- `docs/done/FEATURE-BB60-PHASE01.md`

**Modified**

- `Directory.Packages.props` — the *Text editor* group.
- `src/Enigma.GitClient.Desktop/Enigma.GitClient.Desktop.csproj` — `Avalonia.AvaloniaEdit`.
- `src/Enigma.GitClient.Desktop/App.axaml` — AvaloniaEdit's Fluent theme.
- `src/Enigma.GitClient.Desktop/Themes/Styles.axaml` — the `TextEditor.diff` style.
- `docs/roadmap.md`, `docs/plan/FEATURE-BB60.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the styles reach the editor | A `diff` class it gives itself, and `TextEditor.diff` with `(diff:DiffTextEditor.X)` setters | It wears AvaloniaEdit's template through `StyleKeyOverride`, so its style key is `TextEditor`'s and a `diff\|DiffTextEditor` type selector would match nothing |
| Where the gutter and the renderer read their lines and brushes | From the editor that owns them | One owner and no copies to keep in step. Both are created by the editor and live as long as it does |
| Band header size | The line-number size, in the diff face | It reads as a label, and it follows the font preference like the numbers do; the list used a fixed 11 |
| Seams between tinted rows | Aliased fills | Rows meet on fractional pixels; anti-aliased edges left a faint line between every two tinted rows (seen in a captured frame) |
| `Avalonia.AvaloniaEdit` only, for now | TextMate is referenced in PHASE04 | Nothing uses it before then |

## Deviations & follow-ups

- No deviation from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing the reader sees changed yet. The README's diff-viewer lines stay true; no edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2802 passed**, 0 failed, 0 skipped (12 new).
- Fix budget: no fix cycle.
