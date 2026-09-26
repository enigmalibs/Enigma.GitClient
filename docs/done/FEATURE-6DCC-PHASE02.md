# FEATURE-6DCC-PHASE02 — Conflict resolution engine

**Item:** FEATURE-6DCC — Merge & conflict resolution
**Branch:** `feature/feature-6dcc-phase02-resolution-engine`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

A conflicted file is now something you can decide about. Its three index stages become a
`ConflictDocument`: an ordered list of regions, each either text both sides agree on or a
disagreement carrying the base, our lines and their lines, with a per-region choice — ours, theirs,
both in either order, the base, or text written by hand.

The regions come from `git merge-file --diff3 -p` over the stages, so they are the ones git itself
would have written into the file, with an LCS-based three-way merge as the fallback for when that
output is not available. Rendering the document produces the exact bytes that get written, and it is
the same method that produces the preview — so a preview is truthful by construction rather than by
care. Line terminators travel with their lines, so resolving one conflict in a CRLF file does not
quietly rewrite the other nine hundred lines, and a file with no trailing newline does not gain one.

Writing the decision back is the other half: `ResolveAsync` writes the preview verbatim (UTF-8, no
byte-order mark, no line-ending translation) and stages it, `ResolveWithAsync` takes one whole side
for the conflicts that have no line-by-line answer, `MarkResolvedAsync` stages a file fixed by hand,
and `ResolveAllWithAsync` takes the same side across every conflict left in the repository.

There is no UI yet — that is PHASE03. This phase is the engine underneath it, verified end to end
against real conflicting merges.

## Files / modules touched

**Created — Core**

- `Merging/ConflictDocument.cs` — `ConflictResolution`, `ConflictRegion`, `ConflictMarkers`,
  `ConflictDocument` (`RenderPreview`, `IsFullyResolved`, `ConflictCount`/`ResolvedCount`,
  `ResolveAll`, `SplitLines`, `DetectLineEnding`)
- `Merging/ConflictDocumentBuilder.cs` — `Parse` over git's `--diff3` marker grammar, and `Merge` as
  a direct LCS three-way fallback

**Modified — Core**

- `Merging/ConflictService.cs` — `GetDocumentAsync`, `ResolveAsync`, `ResolveWithAsync`,
  `MarkResolvedAsync`, `ResolveAllWithAsync`, and the private `RunMergeFileAsync`

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-6DCC.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Merging/ConflictDocumentTests.cs` — 34 cases over crafted
  three-way inputs: one region, several regions with stable text between them, a side that deleted
  the lines, leading and trailing conflicts, CRLF preserved exactly, a missing trailing newline,
  every `ConflictResolution` value, `IsFullyResolved` flipping only on the last region, and the
  property-style pair — choosing ours (or theirs) everywhere renders byte-identical to that stage
- `tests/Enigma.GitClient.Core.IntegrationTests/Merging/ConflictResolutionTests.cs` — 15 cases
  against real conflicting merges: the regions git produced, the round trip through `ResolveAsync`
  and `ContinueAsync` with both parents and the resulting tree asserted, `checkout --ours/--theirs`
  for a binary conflict, a delete/modify conflict resolved either way, resolving by hand, the bulk
  helper over a mixed text-and-binary conflict list

**Repaired**

- `tests/Enigma.GitClient.Core.UnitTests/Refs/RefNameValidatorTests.cs` — a raw DEL byte had been
  written into the source instead of its escape text, which makes the file binary to `grep`.
  Replaced with the six-character escape; the test asserts the same thing and still passes.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the regions come from | `git merge-file --diff3 -p` over the three stages, parsed | Its marker grammar is stable and documented, and using it means our regions are the ones git would have written rather than a second opinion about the same file |
| The fallback | An LCS three-way merge in-process | Verified against the same fixtures as the parser, so the shape of the document does not depend on which path produced it |
| A line is a string that keeps its terminator | `SplitLines` never strips the newline | Concatenating the lines reproduces the input byte for byte, which is what makes "the preview is what gets written" a property of the code rather than a promise |
| An unresolved region in the preview | Rendered **with** git's markers | Dropping it would make a half-finished file look finished; showing the markers says exactly what is still undecided, in the place it is undecided |
| A binary file, or a side that does not exist | `GetDocumentAsync` returns `null` | There is no line-by-line answer to "they deleted it and we changed it" — it is a whole-file choice, and the type says so instead of inventing regions |
| Taking a side that deleted the file | `git rm` the path | `checkout --theirs` fails when stage 3 is absent. Keeping "their deletion" means the file is gone; falling back to a removal is the honest reading of the choice, and a test pins it |
| The bulk helper | One side taken whole, per file, via `ResolveWithAsync` | Choosing ours for every region is byte-identical to our stage (the property test says so), and the whole-file form is the only one that also works for the binary conflicts in the same list |
| Encoding on write | UTF-8, no BOM, no translation | The bytes came out of git's index and have to go back unchanged; a BOM or a CRLF rewrite would turn a one-line resolution into a whole-file diff |
| `ResolveAllWithAsync` returning the paths | Returns what it resolved, in order | The UI's report ("resolved 7 files") should come from what happened, not from what was asked for |

## Deviations & follow-ups

- **Deviation:** the plan's step 5 said "bulk helpers … for one file or every file". The one-file
  form is `ConflictDocument.ResolveAll(resolution)`, which is richer than take-ours/take-theirs
  because it covers every `ConflictResolution` value; the every-file form is
  `ResolveAllWithAsync(side)`, which is deliberately limited to the two whole-file sides for the
  reason in the table above.
- **Follow-up:** `ConflictResolution.Custom` is modelled, rendered and tested, but nothing produces
  one yet — the edit box that will is PHASE03's step 3.
- **Follow-up:** `RunMergeFileAsync` writes the three stages to temporary files under the system temp
  directory. It cleans up after itself, but a conflict in a very large file is copied three times to
  disk before it is merged; if that ever matters, `git merge-file` can read from process
  substitution on Linux, which Windows has no equivalent for.
- **Deviation:** no documentation sweep edits were needed. The README already describes conflict
  resolution as a feature of the app, which this phase moves towards rather than contradicts.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1261  failed: 0  succeeded: 1261  skipped: 0
```

49 tests are new in this dev. The integration cases build a real conflicting merge, resolve it
through the same methods the UI will call, and then assert on the repository git was left in — the
merge commit's two parents and the bytes in the work tree — rather than on what the client believed
it had done.
