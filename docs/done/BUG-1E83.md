# BUG-1E83 — Diff freezes when its grammar is removed

**Item:** BUG-1E83 — Diff freezes when its grammar is removed
**Branch:** `bugfix/bug-1e83-diff-grammar-deadlock`
**Run:** bugfix/2026-10-21-diff-grammar-deadlock

## Summary

Selecting or deselecting a file could freeze the application for good: the UI thread deadlocked
against the TextMate tokenizer when a diff editor went from a grammar to none. That transition no
longer takes a grammar away from a live installation.

**Root cause.** `DiffSyntaxHighlighting.SetFile` called `SetGrammar(null)` when the new file had no
grammar: a deselection (`Clear()` → `Title = ""`), or a file such as `.txt`, `.lock` or `.slnx`. In
AvaloniaEdit.TextMate 12.0.0 / TextMateSharp 2.0.3 that call takes the transformer's lock, then the
tokenizer's listeners lock; the tokenizer, finishing a batch on the thread pool, takes them in the
opposite order. The rows are replaced before the title, so the tokenizer was busy at exactly that
moment, and three editors per viewer each took the chance.

**Evidence for the root cause** (standalone harness, outside the repository, not committed): on the
same TextMateSharp 2.0.3, with a transformer copying the library's locking, two runs of three
deadlocked, at attempts 291 and 241 of 500. The route taken here, dispose then reinstall, ran 4 × 500
attempts with no deadlock. No upstream report or fix exists as of 2026-10-21.

**The fix.** When the scope goes from a grammar to none, `DiffSyntaxHighlighting` disposes its
installation and installs a fresh one with no grammar. Disposing takes the two locks one after the
other, never nested, and a fresh installation with no grammar starts no tokenizer. A switch to a real
grammar still goes through `SetGrammar(<non-null>)`, which is safe.

- The editor is kept in a field; `_installation` is no longer `readonly`.
- `Install()` is the one place TextMate is installed, by the constructor and by the reinstall. It
  re-applies the current `Theme` when it differs from the theme the registry was built with, because
  an installation always comes up in the registry's construction theme. Without it, a file going plain
  after a Light/Dark switch would come back in the old theme.
- `Apply`, `Dispose` and `OnFailure` are unchanged and act on the current installation.
- `DiffTextEditor` is unchanged: it keeps the same `DiffSyntaxHighlighting`. A grep confirms
  `DiffSyntaxHighlighting.cs` is still the only `src/` code calling `InstallTextMate` or `SetGrammar`,
  and its one `SetGrammar` call now only ever receives a non-null scope.

**What the tests can and cannot show.** The race itself cannot be tested deterministically: losing it
hangs the UI thread rather than failing the test. The new tests pin the **route** (a new
`TextMateColoringTransformer`, so a reinstall, never `SetGrammar(null)`) and the visible behaviour.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Controls/Diff/DiffSyntaxHighlighting.cs`: the reinstall in `SetFile`,
  `Install()`, the `_editor` and `_registryTheme` fields, a comment at the reinstall and a line in the
  class `<remarks>`.
- `tests/Enigma.GitClient.Desktop.UnitTests/DiffSyntaxHighlightingTests.cs`: four new tests and two
  helpers (`GoesPlainByAReinstallAsync`, `Transformers`). No existing test changed.
  - `AHighlightedFileGoingPlain_IsReinstalled_NotStrippedOfItsGrammar` (theory: `notes.txt`,
    `Makefile`);
  - `ClearingTheFile_GoesPlainTheSameWay` (theory: `null`, `""`);
  - `PlainAndBack_KeepsTheCurrentTheme`;
  - `ClearingTheViewer_ReinstallsEveryPane`.
- `docs/roadmap.md`, `docs/plan/BUG-1E83.md`: statuses.

**Created**

- `docs/done/BUG-1E83.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Waiting for the tokens before going plain (tests 1 and 2) | Yes: line 2 must be coloured first | Plain afterwards then proves the colours came off, not that they never arrived |
| Same `DiffSyntaxHighlighting` after the switch | Asserted (`Assert.Same`) in tests 1 and 2 | The plan's "what gets swapped" decision: only the installation inside it changes |
| Viewer test: `Highlighting` present | `Assert.NotNull` before the scope check | `Highlighting?.Scope` alone would pass on an editor with no highlighting at all |
| Viewer test setup | Repeated inline from `EveryPaneOfTheViewer_IsHighlightedForTheFileItShows` | Extracting a shared helper would have changed an existing test |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Run before tagging, on Linux:** the Desktop suite, in particular `DiffSyntaxHighlightingTests`
  (the four new tests and the existing `AnUnknownExtension_StaysPlain`,
  `AGrammarFailure_TakesTheColoursOff_AndNothingElse`, `AnEditorOffScreen_HasNoHighlightingInstalled`
  and `TheTokensTheme_FollowsTheWindowsThemeVariant`).
- **Worth a click before release:** in the history's details panel, select and deselect files quickly,
  alternating `.cs` files with `.txt` / `.slnx` / extension-less ones, in both renderings.
- **Out of scope, each its own item** (from the plan): hidden editors keep full highlighting; no
  line-length cap; the 5000-line cap skipped in combined-diff hunks; stale reads not cancelled;
  "Show anyway" kept across refreshes; the jump to the first change in the hidden rendering; and
  reporting the lock-order inversion upstream, which is yours to do.
- Line endings: both touched files are LF; no CRLF churn.

## Documentation sweep

Nothing stale. `README.md` mentions syntax highlighting in diffs, which still holds. No `CLAUDE.md` or
`AGENTS.md` exists. The 6.6.0 notes will mention the fix.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: not run (optional for this dev; no Core code touched).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (BUG-6EAA, standing instruction). Four
  tests added (six cases), none changed.
- Fix budget: 0 cycles.
