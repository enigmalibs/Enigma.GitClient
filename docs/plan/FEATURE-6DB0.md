# FEATURE-6DB0 — Core graph, diff & tree algorithms

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** `feature/feature-6db0-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Implement — headless and exhaustively tested — the three algorithms the two headline features depend
on: the commit-graph lane layout, the unified-diff parser with word-level intra-line diffing, and the
path-tree builder that backs the list/tree toggle of the changed-files panel.

## Context & constraints

- These are pure functions over data: no Avalonia types, no process calls, no I/O. That is what makes
  the graph and the diff viewer testable at all.
- The layout engine must be **incremental**: the history page pages commits in, so laying out rows
  1..N then N+1..2N must produce exactly the same result as laying out 1..2N in one call.
- Performance target: 100 000 commits laid out in well under a second, O(rows × activeLanes).

## PHASE01 — Commit graph lane layout

**Status:** DONE

**Steps**

1. Model: `GraphRow` (commit sha, `Lane` of the node, `Colour` index, `IReadOnlyList<GraphEdge>`
   passing through the row, `IsMerge`, `IsCommitNode`), `GraphEdge` (`FromLane`, `ToLane`, `Kind` =
   `Straight` | `MergeIn` | `BranchOut`, `Colour`).
2. `CommitGraphLayout.Build(IEnumerable<GraphCommitInput>, GraphLayoutState? carry)` walks commits in
   the same topological/date order git returned, maintaining a list of *active lanes*, each holding
   the sha it is currently waiting for:
   - a commit whose sha is awaited by one or more lanes takes the **leftmost** such lane; the others
     merge into it (`MergeIn` edges) and are released;
   - a commit awaited by no lane opens a new lane at the first free slot (`BranchOut` for the first
     parent of its child, i.e. a new head);
   - the first parent continues in the commit's own lane; every further parent takes a lane —
     re-using an existing lane already waiting for that parent when there is one, otherwise the first
     free slot;
   - every lane not touched by the row emits a `Straight` edge through it.
3. Colour assignment: a lane keeps its colour for its whole lifetime; colours come from a fixed
   palette cycled by lane index with a small "least recently used" bias so two adjacent lanes rarely
   share a colour. Colour is an **index**, never a brush — the theme owns the actual colours.
4. `GraphLayoutState` captures the active-lane table so the next page continues seamlessly; expose
   `MaxLane` per page for the column width.
5. Orphan/missing parents (shallow clones, filtered logs) terminate their lane cleanly instead of
   waiting forever.

**Acceptance criteria**

- Unit tests over hand-built histories, each asserting the exact `GraphRow` list:
  linear; a simple branch and merge; an octopus merge (3+ parents); two independent roots; criss-cross
  merges; a long-lived branch that keeps its lane and colour across 50 rows; a branch that opens in a
  freed lane after an earlier one closed; a shallow history whose parents are absent.
- Incremental test: `Build(all)` equals `Build(first half)` + `Build(second half, carry)`.
- Performance test: 100 000 synthetic commits lay out in < 1 s (asserted generously to stay stable
  on a loaded machine).

## PHASE02 — Unified diff & word-level diff

**Status:** DONE

**Steps**

1. Model: `FilePatch` (`OldPath`, `NewPath`, `ChangeKind` = `Added`/`Modified`/`Deleted`/`Renamed`/
   `Copied`/`TypeChanged`, `IsBinary`, `OldMode`/`NewMode`, similarity score, `Hunks`,
   `AddedLines`/`RemovedLines`), `DiffHunk` (old/new start + count, section heading, `Lines`),
   `DiffLine` (`Kind` = `Context`/`Added`/`Removed`/`NoNewline`, `OldLineNumber`, `NewLineNumber`,
   `Text`, `Segments`).
2. `UnifiedDiffParser.Parse(string patch)` handles the full `git diff` grammar the app can produce:
   `diff --git` headers with quoted/escaped paths, `similarity index`, `rename from/to`,
   `old/new mode`, `new file`/`deleted file`, `Binary files … differ`, `GIT binary patch`,
   `@@ … @@ section heading`, `\ No newline at end of file`, and combined diffs (`@@@`) from a merge
   commit, which are surfaced as a distinguishable `IsCombined` patch rather than mis-parsed.
3. `WordDiff.Compute(removedLine, addedLine)` — tokenises into words/whitespace/punctuation runs and
   runs a Hirschberg-bounded LCS, producing `Segments` on both lines so the viewer can highlight the
   changed part inside an otherwise similar line. Pairing rule: within a hunk, a run of removed lines
   immediately followed by a run of added lines pairs positionally, and only when the pair's
   similarity exceeds a threshold (otherwise the whole line is highlighted).
4. Guards: a patch larger than a configurable limit (default 5 000 lines per file) is returned with
   `IsTruncated` set and the hunks it did parse, so the UI can offer "show anyway" instead of hanging.

**Acceptance criteria**

- Unit tests over captured real patches: added file, deleted file, pure modification, rename with and
  without content change, copy, mode change, binary file, file with no trailing newline, path with
  spaces and with non-ASCII characters (quoted `"…"` form and `core.quotePath` escapes), multiple
  files in one patch, a hunk with a section heading, and a combined merge diff.
- Line numbers are correct on both sides for every line of every hunk (asserted line by line).
- Word diff: identical lines produce no segments; a one-word change produces exactly one changed
  segment per side; a completely different line falls back to whole-line highlighting.
- Truncation kicks in at the limit and reports it.

## PHASE03 — File path tree builder

**Status:** TODO

**Steps**

1. `FileTreeBuilder.Build(IEnumerable<ChangedFile>)` → a `FileTreeNode` forest (`Name`, `FullPath`,
   `IsDirectory`, `Children`, `Change`), sorted directories-first then case-insensitively by name.
2. Directory **collapsing**: a chain of single-child directories renders as `a/b/c` in one node, the
   way GitKraken and GitHub do, with the collapsing toggleable.
3. Aggregate counts per directory (files added/modified/deleted beneath it) for the folder badges.
4. `ChangedFile` model shared with the diff layer (path, old path, change kind, staged/unstaged flag,
   added/removed line counts, `IsBinary`, `IsConflicted`).

**Acceptance criteria**

- Unit tests: flat list; nested paths; a deep single-child chain collapses (and does not when
  disabled); files and directories at the same level sort directories first; a rename shows the new
  path and keeps the old one; Windows-style separators normalise to `/`; aggregates are correct on
  every directory.

## Out of scope

- Any rendering — the control is FEATURE-2326/FEATURE-7D1B.
- Running git — these functions take strings and records, never a repository.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Diff computation | Parse git's own unified patch | git's diff is authoritative (rename/copy detection, whitespace and algorithm options, submodule handling); re-implementing it would diverge | DiffPlex or a hand-written Myers diff over file contents (new dependency, weaker results, no rename detection) |
| Word-level diff | Small internal LCS over word tokens | Needed only for intra-line highlighting; ~100 lines, fully tested, no dependency | pulling in a diff library for one function |
| Lane assignment | Leftmost free lane, first parent keeps the lane | The rule GitKraken/gitk use; it keeps long-lived branches on a stable column | per-branch fixed lanes (breaks on merges); rightmost-free (visually noisier) |
| Colour model | Palette **index** in Core, brushes in the theme | Keeps Core UI-free and lets Dark/Light pick different actual colours | storing `Color` values in Core |
| Incremental layout | Explicit `GraphLayoutState` carry | The history page pages commits; recomputing the whole graph per page would be O(n²) | recompute everything on each page |
