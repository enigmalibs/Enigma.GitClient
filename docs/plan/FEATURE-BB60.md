# FEATURE-BB60 — Diffs on AvaloniaEdit

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-05-diff-view-avaloniaedit

## Objective

- The diff viewer draws its patch with AvaloniaEdit's `TextEditor`, the editor Enigma.MarkdownEditor
  uses, instead of a `ListBox` of rows built from `TextBlock` line numbers and the custom
  `DiffLineText`.
- What the reader sees today stays:
  - the old and new line numbers and the `+`/`−` marker, in a gutter that never scrolls sideways;
  - the added, removed and filler tints, and the word-level highlight;
  - the hunk bands, which still widen the context when clicked;
  - the minimap, opening at the first change, the whitespace symbols, the tab width and wrapping.
- What the editor adds:
  - native text selection (drag, double-click a word, triple-click a line, Shift+arrows, Ctrl+A);
  - smooth scrolling of one document instead of thousands of templated rows;
  - Ctrl+F search;
  - TextMate syntax highlighting chosen by the file's extension.

## Context & constraints

- **Today's rendering.** `Views/Panels/DiffViewerView.axaml` holds two `ListBox`es (`UnifiedList`,
  `SideBySideList`). Each row template is:
  - a `Button.band` for a hunk header;
  - `TextBlock.diffnumber` × 2 (unified) or × 1 per pane, and a `TextBlock.diffmarker`;
  - a `Controls/Diff/DiffLineText` (668 lines). It draws the line with one `FormattedText`, expands
    tabs, shows whitespace, tints word segments, draws its share of the text selection and applies a
    render-only horizontal offset.

  Around the lists:
  - `DiffMinimap`;
  - the `UnifiedBar` / `LeftBar` / `RightBar` scrollbars, driving `DiffScrollState`, which counts in
    characters;
  - about 300 lines of code-behind re-implementing pointer text selection, which feed
    `DiffRenderOptions.Selection` (`DiffTextSelection`).
- **The ViewModel.** `DiffViewerViewModel` projects a `FilePatch` into `UnifiedRows` /
  `SideBySideRows`, both `ObservableCollection<DiffRowViewModel>` filled row by row:
  - `DiffRowViewModel` is a hunk band or up to two `DiffCellViewModel`s, and `CellFor(DiffPane)` picks
    one;
  - the projection also produces `UnifiedMap` / `SideBySideMap` and the first-change rows;
  - `SelectedText()` builds copy text from `Render.Selection`, skipping bands, fillers and the
    no-newline marker;
  - `CopyCommand` copies the selected text, or else the rows selected in the list (`Selection`,
    `CopySelectionCommand`).
- **The default rendering is side by side.** It reads the whole file (`WholeFileContext`), so its
  expand commands are disabled.
- **MarkdownEditor's precedent.**
  - `Avalonia.AvaloniaEdit` 12.0.0 + `AvaloniaEdit.TextMate` 12.0.0, pinned in their own CPM group:
    they are built against Avalonia 12.0.0 and run on 12.1.x.
  - The Fluent control theme, `avares://AvaloniaEdit/Themes/Fluent/AvaloniaEdit.xaml`, included after
    `FluentTheme`.
  - TextMate Dark+/Light+ chosen from the theme variant (`EditorAppearance`).
- **Colours and typography.** The diff brushes (`Diff*Brush`) are theme resources in
  `Themes/Graph.axaml`. `DiffTypography` publishes `DiffFontFamily`, `DiffFontSize`,
  `DiffLineNumberFontSize`, `DiffGutterWidth` and `DiffMarkerWidth`.
- **Tests run against the real `App`** (`HeadlessAvaloniaFixture`). The baseline is 2790 tests
  passing, 0 warnings.
- **Text.** The parser normalises CRLF to LF. A lone `\r` can still reach a line's text, and it would
  split a document line in two.

## PHASE01 — The diff editor control

**Branch:** `feature/feature-bb60-phase01-diff-editor`
**Status:** DONE — see `docs/done/FEATURE-BB60-PHASE01.md`

### Steps

1. Packages:
   - `Directory.Packages.props` — a *Text editor* group with `Avalonia.AvaloniaEdit` 12.0.0 and
     `AvaloniaEdit.TextMate` 12.0.0, versioned apart from the Avalonia set;
   - the Desktop csproj references `Avalonia.AvaloniaEdit`. TextMate waits for PHASE04.
2. `App.axaml` — include AvaloniaEdit's Fluent theme after `FluentTheme`.
3. `Controls/Diff/DiffDocument.cs` — `DiffDocument.Build(rows, pane)` turns one rendering's rows into
   the document text and one `DiffDocumentLine` per row: kind (hunk header / context / added /
   removed / no-newline / filler), text, old and new number, marker, segments, header text.
   - A band and a filler are empty lines.
   - A lone `\r` becomes `␍`, one character for one, so word segments and selection columns still
     land on the right characters.
4. `Controls/Diff/DiffGutterMargin.cs` — an `AbstractMargin` that draws:
   - the gutter background;
   - the number columns: old and new for the unified pane, one for each side-by-side pane. They are
     right-aligned at `DiffLineNumberFontSize` and sized from `DiffGutterWidth`;
   - the marker column (`DiffMarkerWidth`), in the added and removed marker colours over the row's
     tint;
   - the band colour across a hunk header.

   It redraws when the visual lines change and when the view scrolls.
5. `Controls/Diff/DiffBackgroundRenderer.cs` — an `IBackgroundRenderer` on the background layer. For
   every visible line it draws:
   - the added, removed and filler tints across the whole width;
   - a hunk header as a band, with its header text in the hunk colour at a fixed left inset;
   - each changed word segment, through `BackgroundGeometryBuilder` on the line's offsets.
6. `Controls/Diff/DiffTextEditor.cs` — `DiffTextEditor : TextEditor`, styled as a `TextEditor`. It
   has:
   - `Rows`, `Pane`, `ShowWhitespace` (spaces `·`, tabs `→`) and `TabWidth` (the indentation size);
   - the diff brushes, the gutter metrics and the text-selection brush as styled properties.

   It is read-only:
   - no hyperlinks, no drag and drop, no current-line highlight;
   - nothing scrolls below the document, and it has no undo stack.

   It rebuilds its document once when `Rows` or `Pane` changes. The margin is the first left margin.
7. `Themes/Styles.axaml` — `diff|DiffTextEditor` takes the diff font, the foreground and every brush
   and metric through `DynamicResource`, so the theme and the font preference reach it live.
8. Tests (headless):
   - the document has one line per row, empty band and filler lines, and `\r` replaced;
   - the gutter shows each pane's own numbers and markers;
   - an added row, a removed row, a filler and a band paint their colours, and a changed word paints
     the word colour;
   - whitespace and tab width reach the editor's options;
   - the editor is read-only.

### Acceptance criteria

- A `DiffTextEditor` given a patch's rows and a pane draws numbers, markers, tints, bands and word
  highlights in both themes.
- Nothing in the viewer uses it yet.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — The unified diff on the editor

**Branch:** `feature/feature-bb60-phase02-unified-editor`
**Status:** TODO

### Steps

1. `DiffViewerViewModel`: `UnifiedRows` / `SideBySideRows` become `IReadOnlyList<DiffRowViewModel>`
   properties, replaced once per patch with one change notification, so a document is built once and
   not once per row.
2. `DiffTextEditor`:
   - clicking a hunk band runs its row's `ExpandContextCommand`, with a hand cursor and the band's
     tooltip over it;
   - the editor's selection is mirrored into a `DiffTextSelection` (`Selection` property, row = line −
     1, column = character index);
   - the platform's copy gesture runs `CopyCommand` (a property) instead of the editor's own copy.
3. `DiffViewerView`:
   - the unified grid holds a `DiffTextEditor` bound to `UnifiedRows`, `Pane="Unified"`, the render
     options, `WordWrap` and `CopyCommand`, with the *Copy* context menu;
   - its vertical scrollbar is hidden (the minimap replaces it) and its horizontal one is the
     editor's own;
   - the minimap follows and drives the editor's vertical offset;
   - opening a file scrolls the editor to two lines above its first change;
   - `UnifiedList`, `UnifiedBar` and `DiffRenderOptions.UnifiedScroll` go.
4. Tests:
   - the unified editor draws the patch;
   - a band click widens the context;
   - selecting and pressing Ctrl+C copies plain code without bands;
   - the minimap follows and scrolls the editor, and a file opens at its first change;
   - Ctrl+F opens the search panel;
   - the gutter stays still while the text scrolls sideways.

   The unified `ListBox` tests are rewritten or removed.

### Acceptance criteria

- The unified rendering is a read-only editor: text selectable with the mouse and the keyboard, Ctrl+C
  copies the selected code without markers or bands, Ctrl+F searches.
- Bands widen the context, the minimap and the first-change opening work, and long lines scroll
  sideways with the line numbers fixed.
- Build clean with zero warnings; the whole suite green.

## PHASE03 — Side by side on two editors

**Branch:** `feature/feature-bb60-phase03-side-by-side-editors`
**Status:** TODO

### Steps

1. `DiffViewerView`: the side-by-side grid holds two `DiffTextEditor`s (`Pane="Left"` / `"Right"`)
   either side of the divider.
   - Their vertical and horizontal offsets are kept in step, with a guard against feedback. Each pane
     stops at its own longest line.
   - The minimap and the first-change opening drive both.
2. Wrapping is a unified-only option. The side-by-side editors never wrap, and the toolbar's wrap
   toggle is disabled while side by side is shown: two editors wrapping differently would put the
   rows out of line.
3. One selection at a time: selecting in one pane clears the other's, and both mirror into
   `Render.Selection`.
4. Remove what only the lists used:
   - `SideBySideList`, `LeftBar`, `RightBar`, `DiffScrollState`, `MeasureExtents`;
   - the code-behind's pointer selection, wheel and viewport handling;
   - the row selection (`Selection`, `CopySelectionCommand`, `DiffRowViewModel.Lines`);
   - the list-only styles.

   `CopyCommand` copies the selected text. `DiffLineText` keeps what the conflict page uses — no
   selection, no horizontal offset — and its tests follow.
5. Tests:
   - both panes draw their own side;
   - scrolling one scrolls the other;
   - each pane selects on its own, and Ctrl+C copies that pane's code;
   - the wrap toggle is off side by side;
   - a context line is copied once.

   The drag-selection tests are rewritten for the editors.

### Acceptance criteria

- Side by side is two read-only editors that scroll together vertically and sideways, keep their
  rows aligned, select and copy per pane, and search per pane.
- No `ListBox` or `DiffScrollState` is left in the diff viewer.
- Build clean with zero warnings; the whole suite green.

## PHASE04 — Syntax highlighting in diffs

**Branch:** `feature/feature-bb60-phase04-syntax-highlighting`
**Status:** TODO

### Steps

1. The Desktop csproj references `AvaloniaEdit.TextMate`.
2. `Controls/Diff/DiffSyntaxHighlighting.cs` — installs TextMate on one `DiffTextEditor`:
   - the grammar comes from the file's extension, and an unknown extension stays plain;
   - the theme is Dark+ or Light+ from the editor's actual theme variant, re-applied when it changes;
   - a tokenizing failure is logged through Avalonia's logger and costs that editor its colours on
     the UI thread — never the process.
3. `DiffTextEditor` gets a `FilePath` property (bound to the viewer's `Title`) that sets the grammar,
   and disposes the installation when it is detached.
4. Tests:
   - a `.cs` patch is tokenized: a keyword is coloured differently from plain text;
   - an unknown extension installs no grammar;
   - switching the theme variant switches the TextMate theme;
   - the failure handler drops the highlighting without throwing.
5. Check a `linux-x64` publish carries the `onigwrap` native library.

### Acceptance criteria

- A diff of a file with a known language is syntax-highlighted in both renderings and both themes,
  over the diff's own tints.
- Unknown files stay plain, and a grammar failure never brings the application down.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- The conflict-resolution page, which keeps `DiffLineText`.
- A setting or a toolbar toggle for syntax highlighting.
- Wrapping in the side-by-side rendering.
- Editing a file from the diff.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Which views move | The diff viewer, both renderings | That is the view the question was about. The conflict page draws plain text in three panes and gains little | Migrating the conflict page as well (scope) |
| How the editor carries a diff | `DiffTextEditor : TextEditor`, one document line per row, a gutter margin and a background renderer | Row indices keep their meaning, so the map, the first-change rows and `DiffTextSelection` keep working. Line numbers and tints are exactly what margins and background renderers are for | A row of controls per line (what there is now); embedding controls in lines with `InlineObjectElement` |
| How a hunk band is drawn | An empty document line, painted by the renderer with its header text, and clickable | The grammar never sees `@@` text, a copy has nothing to strip, and the click stays where the reader's hand is | Real `@@` text in the document (it would be tokenized and copied); an inline `Button` per band |
| Side by side | Two editors, scrolled in step | The editor has one column of text; two documents with filler lines are aligned by construction | One editor with both sides interleaved |
| Wrapping side by side | Not offered: the toggle is disabled there | Two editors wrap independently, so wrapped rows would fall out of line, which defeats the rendering | Wrapping each side and accepting misaligned rows |
| Selection and copy | The editor's native selection, mirrored into `DiffTextSelection`; the copy gesture runs `CopyCommand` | Copied text keeps today's shape — code only, no bands, no fillers, no markers — and the copy tests stay valid | The editor's own copy (blank lines for bands and fillers) |
| Row multi-selection | Removed | With text selection there are no rows to select; one selection model is enough | Keeping row selection beside text selection |
| Caret | Visible, no current-line highlight | Keyboard selection needs a caret to see where it is | A hidden caret |
| Search | AvaloniaEdit's built-in Ctrl+F panel, per pane | Comes with the editor; no new UI | A viewer-level search box |
| Syntax highlighting | Always on, TextMate by extension, Dark+/Light+ by theme variant | MarkdownEditor's choice; unknown languages stay plain | A setting or a toggle (gold-plating for now) |
| Grammar failure | Logged, highlighting dropped for that editor | Tokenizing runs on a background thread; an unhandled failure would end the process | No handler |
| Row lists | `IReadOnlyList` replaced once per patch | A document is built once and not once per added row | Debouncing collection changes in the control |
| Package versions | `Avalonia.AvaloniaEdit` / `AvaloniaEdit.TextMate` 12.0.0 in their own CPM group | Built against Avalonia 12.0.0 and already running on 12.1.x in MarkdownEditor; they version apart from the Avalonia set | Holding them in the Avalonia group |
| A lone `\r` | Shown as `␍` | One character for one keeps segment and selection offsets exact, and keeps one document line per row | Dropping it (shifts offsets) |
| Native dependency | `onigwrap` shipped by the RID publish `install.sh` already does | TextMate's regex engine is native; a publish check proves it lands | Nothing to change |
