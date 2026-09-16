# FEATURE-6DB0-PHASE02 — Unified diff & word-level diff

**Item:** FEATURE-6DB0 — Core graph, diff & tree algorithms
**Branch:** `feature/feature-6db0-phase02-diff-parser`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Implemented the model and parser behind the second headline feature: git's unified patch grammar in
full, plus a bounded word-level diff that finds the edit inside a changed line so the viewer can
highlight it rather than painting whole lines one colour.

Every payload in the unit tests was **captured from a real `git diff` run**, and a companion
integration suite re-runs the parser against a live git so the captured payloads cannot silently
drift from what the installed git emits.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/Diff/`**

- `DiffModel.cs` — `FileChangeKind`, `DiffLineKind`, `DiffSegment`, `DiffLine`, `DiffHunk`,
  `FilePatch`, `PatchSet`
- `GitPathQuoting.cs` — reverses git's C-style path quoting, including octal escapes
- `UnifiedDiffParser.cs` — `DiffParseOptions` and the parser
- `WordDiff.cs` — intra-line segments and the hunk pairing pass

**Created — tests**

- Unit: `UnifiedDiffParserTests` (32 cases), `WordDiffTests` (20), `GitPathQuotingTests` (17)
- Integration: `UnifiedDiffParserAgainstRealGitTests` (10), driving a live git

**Modified**

- `docs/roadmap.md`, `docs/plan/FEATURE-6DB0.md` — status updates

## Grammar covered

`diff --git` and the combined `diff --cc` / `diff --combined` forms · `old mode` / `new mode` ·
`new file mode` / `deleted file mode` · `similarity index` / `dissimilarity index` ·
`rename from` / `rename to` · `copy from` / `copy to` · `index <a>..<b> <mode>` ·
`Binary files … differ` and `GIT binary patch` · `--- a/…` / `+++ b/…` including `/dev/null` ·
`@@ -l,s +l,s @@ heading` with the count omitted · combined `@@@ … @@@` with one marker column per
parent · `\ No newline at end of file` · submodule entries (mode `160000`) · quoted and non-ASCII
paths · paths containing spaces.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where paths come from | The `---`/`+++` lines whenever present, with the `diff --git` line only as a fallback | `diff --git a/a file.txt b/a file.txt` is **genuinely ambiguous** for a path containing a space. The `---`/`+++` lines carry one path each and are unambiguous. The fallback prefers the split that makes both halves equal, which resolves the only case where those lines are absent (a pure mode change or a pure rename) |
| Word-diff algorithm | Shared prefix/suffix trimming first, then a bounded LCS over word tokens on what remains | Prefix/suffix trimming alone resolves the overwhelming majority of real edits at O(n) with no allocation. The LCS then handles multi-edit lines. Past 96 tokens a side it falls back to highlighting the trimmed middle — the plan said "Hirschberg-bounded"; a token cap achieves the same intent (bounded memory) in a fraction of the code, and the fallback still benefits from the trimming, so a one-word change in a 300-token line still highlights one word |
| Dissimilar lines | No segments at all | Two lines sharing less than 25% of their text were replaced, not edited. Highlighting nearly all of both would be noise; the viewer's plain add/remove colouring is the honest rendering |
| Pairing inside a hunk | A run of removed lines pairs positionally with the run of added lines that immediately follows | Exactly what `git --word-diff` does, and what an edit actually looks like in a patch. A context line between them breaks the pairing, which a test asserts |
| Combined merge diffs | Parsed, flagged `IsCombined`, markers stripped, line numbers left null | A combined diff has one marker column per parent and no single old side, so pretending it is an ordinary patch would produce wrong line numbers. Flagging it lets the viewer say which diff it is showing |
| Truncation | Per-file line budget, then resynchronise to the next `diff --git` | A generated file can run to millions of lines. The budget keeps the hunks already parsed, marks the file truncated, and — critically — the **next** file still parses, which a test asserts |
| Octal escapes in quoted paths | Decoded into a byte buffer and interpreted as UTF-8 once at the end | `\303\251` is one `é`. Decoding each escape separately would produce two replacement characters — the classic mojibake bug |
| `DiffLine.Segments` setter | `internal set` | The segments are filled in by a second pass over an already-built hunk; making them constructor-only would mean building every line twice |

## Deviations & follow-ups

- **Deviation:** the plan specified a Hirschberg-bounded LCS. A token cap plus prefix/suffix trimming
  was implemented instead — same bounded-memory guarantee, far less code, and the same result on
  every case the tests cover (see the decision table).
- **Deviation (additive):** `DiffParseOptions.ComputeWordDiff` was added so a caller that only wants
  file statistics (the changed-files panel) does not pay for intra-line analysis.
- **Deviation (additive):** the live-git integration suite was not in the plan. Captured payloads
  that nothing re-verifies are how a parser quietly rots when a tool changes its output; ten
  integration tests keep them honest, including a cross-check of every file's `+`/`−` counts against
  `git diff --numstat -z`.
- **Follow-up:** the parser reads a patch that is already fully in memory. For a very large diff the
  viewer will want a streaming reader; the truncation budget makes that unnecessary for now.
- **Follow-up:** `GIT binary patch` payloads are recognised and skipped, not decoded. Decoding them
  would only be useful for applying a patch, which this client does not do.
- **Line endings (recommendation only):** the parser normalises CRLF **in the patch payload** before
  splitting, which is required to parse a patch produced on Windows; it does not alter file content.
  No CRLF churn observed in the repository. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 373  failed: 0  succeeded: 373  skipped: 0
```

87 tests are new in this dev (69 unit, 10 integration, plus 8 theory rows). One fix cycle was used:
the numstat cross-check parsed the human-readable rename shorthand (`dir/{old => new}`) and failed to
match a file. It was switched to `--numstat -z`, whose rename records are unambiguous, and a guard
was added so a loop that silently compared nothing can no longer pass. The parser itself was not at
fault. The warning count was read explicitly (0).
