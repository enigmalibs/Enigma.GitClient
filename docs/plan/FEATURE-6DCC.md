# FEATURE-6DCC — Merge & conflict resolution

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-6dcc-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Merge branches, and when a merge conflicts, resolve it properly: a three-way view of base, ours and
theirs, per-hunk selection of what to keep, and a live preview of exactly the file that will be
written.

## Context & constraints

- Rebase is forbidden, so merge is *the* integration operation in this app and has to be good.
- The three sides come from the index stages — `git show :1:<path>` (base), `:2:` (ours), `:3:`
  (theirs) — not from parsing conflict markers out of the work-tree file. That is the only way to get
  a real base, and it is what makes an exact preview possible.
- Conflict hunks are computed by `git merge-file --diff3 -p` over the three stages, whose output has a
  stable marker grammar; binary and add/add conflicts are handled as whole-file choices.

## PHASE01 — Merge engine & conflict model

**Steps**

1. `IMergeService`: `MergeAsync(MergeRequest)` (`--no-ff` / `--ff` / `--squash`, custom message,
   `--no-commit`), `AbortAsync`, `ContinueAsync`, `IsMergeInProgressAsync`, and
   `GetMergeHeadsAsync` (ours/theirs sha and their friendly names).
2. `MergeOutcome`: `FastForward`, `Merged`, `AlreadyUpToDate`, `Conflicted(files)`, `Failed(reason)`,
   parsed from git's exit code plus stdout/stderr rather than guessed.
3. `IConflictService.GetConflictsAsync()` — from `status --porcelain=v2` `u` records: path, the two
   stage modes, conflict kind (`BothModified`, `AddedByUs`, `AddedByThem`, `DeletedByUs`,
   `DeletedByThem`, `BothAdded`, `BothDeleted`), and `IsBinary`.
4. `IConflictService.GetSidesAsync(path)` → `ConflictSides` (base/ours/theirs text or `null` when that
   stage does not exist, plus the encoding and a binary flag), read through `git show :n:<path>` with
   raw bytes so encodings survive.
5. UI hook: merge actions on the branches page and the graph context menu ("Merge <branch> into
   <current>"), with the outcome reported in the InfoBar and a conflicted outcome switching the shell
   into conflict mode (a banner with Resolve / Abort).

**Acceptance criteria**

- Integration tests: fast-forward merge; true merge with a generated message; `--no-ff` forces a merge
  commit; already-up-to-date; a conflicting merge reports `Conflicted` with the right file list;
  abort restores the pre-merge state exactly (HEAD, index and work tree compared); continue commits
  the merge after resolution; every conflict kind above is produced by a fixture and classified
  correctly; `GetSidesAsync` returns the right three blobs, and `null` for the missing stage of an
  add/add or delete/modify conflict.

## PHASE02 — Conflict resolution engine

**Steps**

1. `ConflictDocument` — built from `ConflictSides`: a list of `ConflictRegion`s, each either a
   `Stable` region (identical in ours and theirs) or a `Conflicted` region carrying the base, ours and
   theirs line ranges and a per-region `Resolution` (`Ours`, `Theirs`, `OursThenTheirs`,
   `TheirsThenOurs`, `Base`, `Custom(text)`, `Unresolved`).
2. `ConflictDocumentBuilder` parses `git merge-file --diff3 -p` output into that model, and falls back
   to a direct three-way LCS over the stages when merge-file is unavailable for the path.
3. `ConflictDocument.RenderPreview()` — produces the exact bytes that would be written, honouring the
   original line endings and the absence of a trailing newline; `IsFullyResolved` reports whether any
   region is still `Unresolved`.
4. `IConflictService.ResolveAsync(path, text)` writes the file and `git add`s it;
   `ResolveWithAsync(path, side)` uses `git checkout --ours/--theirs` for the whole-file cases;
   `MarkResolvedAsync(path)`.
5. Bulk helpers: take all-ours / all-theirs for one file or every file.

**Acceptance criteria**

- Unit tests over crafted three-way inputs: a single conflicting region; several regions separated by
  stable text; a region where one side deleted the lines; leading/trailing conflicts; CRLF input
  preserved exactly; a file with no trailing newline preserved; every `Resolution` value renders the
  expected preview; `IsFullyResolved` flips only when the last region is resolved.
- Property-style test: choosing `Ours` for every region renders byte-identical to the ours stage, and
  the same for `Theirs`.
- Integration tests: resolve a real conflict through `ResolveAsync`, then `ContinueAsync`, and assert
  the resulting tree and the merge commit's two parents; `checkout --ours`/`--theirs` paths work for
  binary conflicts.

## PHASE03 — Conflict resolution UI

**Steps**

1. `ConflictResolutionPageViewModel` — conflicted files list (kind, icon, resolved state), the
   selected file's `ConflictDocument`, per-region resolution commands, bulk commands, the preview, and
   the Continue/Abort commands gated on every file being resolved.
2. Three-pane view: **Ours** (left), **Base** (centre, collapsible), **Theirs** (right), each rendered
   with the diff viewer's line rows and tinted by which side a region comes from; the panes scroll in
   sync by region.
3. Per-region controls rendered between the panes: `Take ours`, `Take theirs`, `Take both (ours
   first)`, `Take both (theirs first)`, `Take base`, and `Edit…` opening the region in an editable box.
4. **Preview pane** below (or as a fourth tab) showing the exact resulting file, updating live as
   regions are resolved, with unresolved regions marked in place.
5. Whole-file conflicts (binary, add/add, delete/modify) render a card with the two (or one) available
   choices and the file sizes/dates, not a three-pane text view.
6. Keyboard: `Alt+←`/`Alt+→` take ours/theirs for the focused region, `Alt+↓` next unresolved region.
7. The banner in the shell shows "N of M conflicts resolved" and only enables "Commit merge" at N = M.

**Acceptance criteria**

- Unit tests with a faked service: region commands set the right `Resolution`; the preview text
  matches `RenderPreview()`; Continue is disabled until every file is resolved and every region within
  them is; Abort confirms first; bulk take-all-ours resolves every region of every file.
- Headless render test: the three panes and the preview build for a real conflict document, and
  region tints resolve distinct theme brushes in both variants.
- Manual smoke: a real conflicting merge is resolved end to end from the UI.

## Out of scope

- Rebase conflict resolution — rebase does not exist in this product.
- External merge-tool launching (follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Conflict source of truth | Index stages 1/2/3 via `git show` | Gives a genuine merge base and exact bytes; work-tree marker parsing loses the base and breaks on content that looks like a marker | parsing `<<<<<<<` markers from the work-tree file |
| Region computation | `git merge-file --diff3 -p`, with an internal three-way LCS fallback | Reuses git's own merge semantics so our regions match what git would have written | only our own three-way diff (would disagree with git) |
| Preview | Render the exact bytes from the resolution model | "A preview of what will be done" is an explicit requirement, and rendering from the same function that writes the file makes it truthful by construction | reconstructing the preview separately from the write path |
| Line endings | Preserved from the ours stage | Resolving a conflict must not rewrite the whole file's endings — that is the classic diff-noise bug | normalising to LF |
| Merge default | `--no-ff` offered but not forced; git's default otherwise | Keeps the history git would produce unless the user asks otherwise | forcing `--no-ff` (surprising); forcing `--ff-only` (fails on real merges) |
