# FEATURE-BB60-PHASE04 — Syntax highlighting in diffs

**Item:** FEATURE-BB60 — Diffs on AvaloniaEdit
**Branch:** `feature/feature-bb60-phase04-syntax-highlighting`
**Run:** feature/2026-10-05-diff-view-avaloniaedit

## Summary

Every diff editor — the unified one and both sides of the side-by-side rendering — is now
syntax-highlighted with TextMate. The diff's own tints, bands and word highlight stay behind the
text. A keyword on an added line is green behind and keyword-blue in front. This finishes
FEATURE-BB60.

- **The grammar** comes from the file's extension: `DiffTextEditor.FilePath`, bound to the viewer's
  `Title`, so a rename is highlighted by its new name. An extension no bundled grammar knows — or no
  extension at all — stays plain.
- **The theme** is Visual Studio Code's Dark+ or Light+, from the editor's actual theme variant, as in
  Enigma.MarkdownEditor. It is re-applied live when the window's theme changes. Text no token claims
  keeps the diff's own foreground.
- **A failure.** If a grammar throws while it tokenizes (on a background thread), the failure is
  logged under the `DiffSyntax` area and the highlighting is taken off that editor on the UI thread.
  The diff stays, plain.
- **Lifetime.** `DiffSyntaxHighlighting` is installed when an editor is attached and disposed when it
  is detached, so no tokenizer runs for an editor nobody can see.
- **Shipping.** `AvaloniaEdit.TextMate` brings TextMateSharp and the native `onigwrap` regex library.
  A `linux-x64` publish carries `libonigwrap.so` and a `win-x64` publish carries `libonigwrap.dll`,
  so `install.sh`'s RID publish needs no change.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffSyntaxHighlighting.cs`
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffSyntaxHighlightingTests.cs` — 8 tests:
  - a C# file is tokenized: a keyword and a comment in their own colours, apart from plain text;
  - an unknown extension, and no extension, stay plain;
  - the TextMate theme follows the window's variant both ways;
  - a grammar failure takes the colours off and nothing else;
  - an editor off screen has no highlighting, and gets it back with its grammar;
  - every pane of the viewer is highlighted for the file it shows;
  - `ThemeNameFor` maps both variants.
- `docs/done/FEATURE-BB60-PHASE04.md`

**Modified**

- `src/Enigma.GitClient.Desktop/Enigma.GitClient.Desktop.csproj` — `AvaloniaEdit.TextMate`.
- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffTextEditor.cs` — `FilePath`, `Highlighting`,
  installed on attach, re-themed on a variant change, disposed on detach.
- `src/Enigma.GitClient.Desktop/Views/Panels/DiffViewerView.axaml` — each editor's `FilePath` binds
  `Title`.
- `docs/roadmap.md`, `docs/plan/FEATURE-BB60.md` — PHASE04 and FEATURE-BB60 are `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One registry for all editors, or one each | One each, as MarkdownEditor does | The installation applies the registry's default theme as it is built, and the tokenizer reads the registry from a background thread; one per editor keeps both simple and safe. The cost is three per viewer |
| When the highlighting is installed | On attach, disposed on detach | The tokenizer is a background job; an editor off screen, such as the rendering not shown, has nothing to colour |
| Following the theme | `ActualThemeVariantChanged`, ignoring the moment the variant is null | A probe showed a control leaving the tree loses its inherited variant before it is told it is detached, and applying "no variant" threw |
| Which path picks the grammar | The viewer's `Title` (the patch's display path) | It is the new path for a rename, the file as it is now |
| How a failure is reported | Avalonia's logger, area `DiffSyntax`, warning level | The control has no DI logger, and a lost colour is not something to interrupt the reader with |

## Deviations & follow-ups

- **Unified view, as planned.** Its one document mixes old and new lines, so the grammar can read an
  added line as the continuation of the removed line above it. In the test, `public sealed class New`
  under `public sealed class Old` stays plain. The side-by-side sides are each one file's lines and
  highlight cleanly. A follow-up could tokenize the old and new sides apart and colour the unified
  lines from them.
- The README and the release notes do not yet mention syntax highlighting or Ctrl+F search. They
  belong in the next release item's *What's new*, which is how this project records features.
- Line endings: no CRLF churn.

## Documentation sweep

Nothing in the README, the agent instructions or `docs/` was made wrong by this dev. No edit (see the
follow-up above).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2800 passed**, 0 failed, 0 skipped (8 new).
- `dotnet publish src/Enigma.GitClient.Desktop -c Release -r linux-x64 --self-contained false` →
  `libonigwrap.so`, `Onigwrap.dll`, `TextMateSharp*.dll`, `AvaloniaEdit*.dll` present. Same for
  `win-x64` → `libonigwrap.dll`.
- Captured frames of a C# diff side by side in Dark and Light: keywords, types, strings, comments and
  doc tags coloured over the added/removed tints and the word highlight, all legible.
- Fix budget: 2 cycles.
  1. Guard against the null theme variant during detach. It crashed every test that closed a viewer.
  2. The tokenizing test checks the removed line's keyword, after a probe showed the added line
     beneath it is read as the rest of the removed line's declaration.
