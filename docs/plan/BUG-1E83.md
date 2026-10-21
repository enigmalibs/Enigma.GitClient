# BUG-1E83 — Diff freezes when its grammar is removed

**Status:** DONE — see `docs/done/BUG-1E83.md`
**Type:** BUG
**Branch:** `bugfix/bug-1e83-diff-grammar-deadlock`
**Run:** bugfix/2026-10-21-diff-grammar-deadlock

## Objective

Selecting or deselecting a file in the history's details panel (a commit's files, or the working
tree's *Not staged* / *Staged* lists) sometimes freezes the whole application for good. The UI thread
deadlocks inside the syntax highlighting of the diff viewer. After this fix, no switch of the diff
from one file to another, or to no file, can deadlock. Highlighting keeps working as it does today.

## Context & constraints

**Root cause (established, not re-investigated).** `DiffSyntaxHighlighting.SetFile` calls
`_installation.SetGrammar(scope)`, and `scope` is `null` when the new file has no grammar.
AvaloniaEdit.TextMate 12.0.0 and TextMateSharp 2.0.3 then take two locks in opposite orders on two
threads:

| Thread | First lock | Then |
|---|---|---|
| UI: `Installation.SetGrammar(null)` → `TextMateColoringTransformer.SetGrammar` | the transformer's `_lock` | `TMModel.SetGrammar(null)` → `Stop()` → `Emit(...)` → `lock (listeners)` |
| Tokenizer (thread pool): `TMModel.Emit` after a 5 ms token batch | `lock (listeners)` | `TextMateColoringTransformer.ModelTokensChanged` → the transformer's `_lock` |

If the tokenizer emits inside the UI thread's short window, each thread waits for the other forever,
at 0 % CPU. Only a switch **to no grammar** emits from the UI thread; a switch to another real grammar
stops and restarts the tokenizer without emitting.

Why selection changes hit it:

- **Deselecting** calls `DiffViewerViewModel.Clear()`, which sets `Title` to `""`. `FilePath` is bound
  to `Title`, so this reaches `SetFile("")`, then `SetGrammar(null)`.
- **Selecting a file with no grammar** does the same: `.txt`, `.lock`, `.slnx`, `.config`, `.resx`,
  binaries and images, files with no extension. (Checked against the bundled registry: `.txt`, `.lock`
  and `.slnx` map to no scope.)
- **The tokenizer is busy at exactly that moment.** `DiffViewerViewModel.Apply` replaces the rows
  before it sets `Title`; the new documents restart the tokenizer with the old grammar.
- **Three times per switch.** `UnifiedEditor`, `LeftEditor` and `RightEditor` all bind `FilePath` to
  `Title` and stay attached when hidden.

**Evidence** (cited in the completion doc; the harness lives outside the repository and is not
committed): a standalone harness on the same TextMateSharp 2.0.3, with a transformer copying the
library's locking, deadlocked in two runs of three (attempts 291 and 241 of 500). Dispose-then-reinstall
ran 4 × 500 attempts with no deadlock. No upstream report or fix exists as of 2026-10-21.

**Constraints:**

- Do not run `Enigma.GitClient.Desktop.UnitTests` on this Windows machine, not even a subset
  (BUG-6EAA). Compile it; report the Desktop tests as **compiled, not run**, and recommend a Linux run
  before tagging. Core unit tests run; the Core integration tests are optional (no Core code touched).
- No new package, no version bump of Avalonia, AvaloniaEdit or TextMate.
- Match the surrounding code: comment density and voice, naming, `ArgumentNullException.ThrowIfNull`.

## Steps

1. `src/Enigma.GitClient.Desktop/Controls/Diff/DiffSyntaxHighlighting.cs`:
   - Keep the editor the instance was built for (a `TextEditor` field); `_installation` stops being
     `readonly`.
   - One private install method: `editor.InstallTextMate(_registry, initCurrentDocument: true,
     OnFailure)`, then re-apply the current `Theme` when it is not the theme the registry was
     constructed with (`GetDefaultTheme()` is fixed at construction). The constructor uses it too.
   - `SetFile(path)`:
     - same scope as before → unchanged;
     - new scope `null`, current not → dispose the installation, install a fresh one, `Scope = null`.
       **No `SetGrammar` call at all**;
     - new scope not `null` → `_installation.SetGrammar(scope)`, as today.
   - `Apply(variant)`, `Dispose()` and `OnFailure` keep their behaviour, on the current installation.
   - Comments in the file's own style, citing `BUG-1E83`: one at the reinstall (the lock-order
     inversion and its two code paths), one line in the class `<remarks>`.
2. `DiffTextEditor` is unchanged: it keeps the same `DiffSyntaxHighlighting`, only the installation
   inside is swapped. Re-check with a grep that no other `src/` code calls `InstallTextMate` or
   `SetGrammar`.
3. Tests in `tests/Enigma.GitClient.Desktop.UnitTests/DiffSyntaxHighlightingTests.cs`, reusing its
   helpers (`Show`, `Settle`, `WaitUntilAsync`, `ForegroundAt`, `Themed`) and naming style:
   1. **A highlighted file going plain is reinstalled, not stripped of its grammar** — from
      `src/App.cs` to an unknown extension and to no extension: `Scope` is `null`, `IsDisposed` is
      false, exactly one `TextMateColoringTransformer`, a different instance from before; line 2 is
      drawn in the plain foreground.
   2. **Clearing the file goes plain the same way** — `FilePath = null`, and `""` (what
      `DiffViewerViewModel.Clear()` produces).
   3. **Plain and back keeps the current theme** — under Dark, show `src/App.cs`, switch to Light, go
      to `notes.txt` and back: `Theme == LightPlus`, and once the tokens arrive the keyword at line 2
      col 0 has the colour of a second editor shown fresh under Light.
   4. **Viewer level** — after `viewer.Clear()`, each of `UnifiedEditor`, `LeftEditor`, `RightEditor`
      has `Scope` `null`, is not disposed, and has exactly one `TextMateColoringTransformer`.
   - The race itself cannot be tested deterministically (a lost race hangs the UI thread rather than
     failing). The tests pin the **route** and the visible behaviour; the completion doc says so.
4. The existing tests stay unchanged — notably `AnUnknownExtension_StaysPlain`,
   `AGrammarFailure_TakesTheColoursOff_AndNothingElse`, `AnEditorOffScreen_HasNoHighlightingInstalled`
   and `TheTokensTheme_FollowsTheWindowsThemeVariant`. No assertion is weakened.

## Acceptance criteria

- Every transition to no grammar replaces the installation: grammar to unknown extension, to no
  extension, to `null` or `""`.
- Nothing in `src/` calls `SetGrammar` with a scope that can be `null`.
- After any number of switches, each editor has exactly one `TextMateColoringTransformer`, and the
  disposed installations leave nothing behind.
- Switching back to a highlighted file colours it again, in the theme the window currently shows.
- Grammar-to-grammar switches, theme following, the off-screen take-off and the failure path behave
  exactly as before.
- The four new tests exist and compile; the existing tests are unchanged.
- The full solution builds with zero warnings; the Core unit tests are green.

## Out of scope

Follow-ups, each its own item:

- **Hidden editors keep full highlighting.** The hidden rendering's editors, and all three while the
  diff view is closed, keep live installations (`DiffTextEditor.cs:288-291` says otherwise; `IsVisible`
  never detaches).
- **No line-length cap** (`DiffDocument.cs:121`): a minified file can stall the UI for seconds.
- **The 5000-line cap is skipped inside a combined-diff hunk** (`UnifiedDiffParser.cs:345-361`).
- **Stale reads are not cancelled** (`DiffViewerViewModel.ReadAsync` passes `CancellationToken.None`).
- **"Show anyway" stays on across automatic refreshes.**
- **The jump to the first change does nothing in the hidden rendering's editors**
  (`DiffViewerView.axaml.cs:162`).
- **Reporting the lock-order inversion upstream** (AvaloniaEdit / TextMateSharp) — outward-facing, the
  user's to do.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Item shape | One `BUG`, single phase | One class and one test file: one reviewable commit | Splitting fix and tests (a fix without its tests is not reviewable alone) |
| Where to fix (given) | In `DiffSyntaxHighlighting` | One class owns every grammar change; no upstream fix exists | Patching or forking AvaloniaEdit.TextMate; waiting for upstream |
| Reaching plain (given) | Dispose, then reinstall with no grammar | The dispose path never nests the two locks; proven by the harness | A grammar that colours nothing; `SetGrammar(null)` only when idle (no API to know) |
| What gets swapped (given) | The installation inside the same `DiffSyntaxHighlighting` | `DiffTextEditor` and its tests stay untouched | Recreating `DiffSyntaxHighlighting` from the editor |
| Registry on reinstall (given) | Reuse `_registry` | Constructing one indexes every bundled grammar | A new `RegistryOptions(Theme)` per reinstall |
| Knowing the registry's default theme | A readonly field holding the `ThemeName` it was built with | `GetDefaultTheme()` returns a raw theme, not a name to compare | Comparing raw themes; always reloading (a needless parse) |
| Covering "grammar to no extension" | Test 1 is a theory over `notes.txt` and `Makefile` | The acceptance criteria name it; same assertions, one more data row | A fifth test |
| Test strength at viewer level | Also assert a different transformer per pane | It pins the route, not only the end state | The three end-state assertions alone |
| How to test the deadlock (given) | Pin the route and the behaviour; cite the harness | A lost race hangs the run instead of failing it | A stress test in the headless suite |
