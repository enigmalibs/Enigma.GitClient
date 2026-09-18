# FEATURE-295F-PHASE02 — The whole file, side by side

**Item:** FEATURE-295F — Diff viewer: sync, paths, full file
**Branch:** `feature/feature-295f-phase02-whole-file`
**Run:** feature/2026-09-18-history-and-diffs

## Summary

Side by side now shows the whole file on both sides, not the changed parts alone. Unified is
untouched and still shows what changed, with the reader's own context and its expand buttons.

Nothing new reaches git. `WholeFileContext` — the context-line count that means "all of it" — already
existed for the `Expand all` button, and `ContextLines` is what `ReloadAsync` hands over. The change
is one property: `EffectiveContextLines` is `WholeFileContext` while the side-by-side rendering is
shown, and `ContextLines` otherwise. `ContextLines` itself is left alone underneath, so switching to
unified goes straight back to the preference the reader set rather than to some remembered
expansion.

Switching the rendering therefore re-reads the patch, because the two shapes now ask git different
questions and rendering the rows already in hand in the other shape would show the wrong amount of
file. The existing generation counter already discards an answer that arrives after the reader has
moved on, so a switch mid-read needs nothing new.

Both expand-context commands refuse while the whole file is already shown — widening whole-file
context would re-read an identical patch — and the hunk band, which runs the same command, does
nothing rather than pretending to.

One ordering change came with it: taking the stored preferences on (`Apply(_settings.Current)`) now
happens at the *end* of the constructor. It sets `ViewMode`, whose setter now tells the expand
commands their answer changed, and those commands did not exist yet where the call used to sit — and
the stored default is side by side, so this was not a theoretical order.

Very large files reach `MaxLinesPerFile` sooner with whole-file context than with three lines of it.
That is already handled: the viewer reports itself truncated and offers `Show anyway`, which re-reads
without the limit. The existing test for that path still passes.

## Files / modules touched

**Modified — App**

- `ViewModels/Panels/DiffViewerViewModel.cs` — `EffectiveContextLines`; `ReloadAsync` asks for it;
  `ViewMode`'s setter re-reads and re-evaluates the expand commands; both expand commands refuse
  once the whole file is shown; `Apply(_settings.Current)` moved to the end of the constructor

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/DiffViewerTests.cs` — a `UnifiedAsync` harness helper for
  the context tests, which are about the unified rendering;
  `Viewer_SwitchesShapeWithoutRereadingThePatch` → `Viewer_SwitchesShapeAndRereadsForIt`;
  `Viewer_AsksForTheDefaultContextFirst` split into `Viewer_AsksForTheWholeFileSideBySide` and
  `Viewer_AsksForTheDefaultContextUnified`; new `Viewer_CannotExpandWhatIsAlreadyTheWholeFile`; and
  a real-repository `Viewer_ShowsTheWholeFileSideBySideAndTheChangeAloneUnified` that renders a
  200-line file with one edited line and counts the rows on both sides

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-295F.md`, `README.md`, `RELEASENOTES.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the whole-file decision lives | An `EffectiveContextLines` the reload asks for | `ContextLines` is the reader's preference and stays theirs; overwriting it would make the unified rendering inherit a context nobody chose |
| Switching shape | Re-reads | The two shapes are two questions for git now; drawing the old answer in the new shape would show the wrong amount of file |
| Proving the rendering | A real repository, not the recording double | The double returns whatever patch it was handed whatever context was asked for, so it can prove the *request* but never the rows. Counting 200 rows per side needs git to have produced them |
| The constructor's order | `Apply(_settings.Current)` last | It sets `ViewMode`, whose setter now touches the expand commands, and the stored default is side by side — so the old position would have dereferenced a command not yet built |
| Very large files | Left to the existing truncation band | Whole-file context hits the parse limit sooner, and `Show anyway` is already the answer to that |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are covered.
- **The hunk band is still drawn side by side**, now as a single band covering the whole file. It is
  harmless — one row at the top — and its click is a no-op because the expand command refuses.
  Hiding it in that rendering was not asked for and would be a second behaviour to explain.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1681  failed: 0  succeeded: 1681  skipped: 0
```

Three tests are new and six changed. One fix cycle was needed: five context tests asserted the
default rendering asked for three lines of context, which is now the unified rendering's question
rather than the one the viewer opens on.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Both describe the diff as "unified or side by side" and
the release notes say "Expand the context around a change, or open the whole file" — a sentence that
now describes the unified rendering only, since side by side is always the whole file. The release
notes' diff section and the README's feature list each gain a line saying what the two shapes now
show and that the two sides scroll as one (PHASE01, which held its line back for this). There is no
`CLAUDE.md`, `CHANGELOG.md` or `CONTRIBUTING.md` in the repository.
