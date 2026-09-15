# FEATURE-7D1B — Commit details & diff viewer

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** `feature/feature-7d1b-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

The second headline feature: selecting a commit (or a merge) lists the files it touched — as a **list
or a tree, the user's choice** — and selecting a file shows a colour-coded diff with added, removed,
context and intra-line changes clearly distinguished.

## Context & constraints

- The patch model and the word-level diff come from FEATURE-6DB0 PHASE02; this item renders them.
- Diff colours come from the theme (`EnigmaSuccess*` / `EnigmaError*` families plus dedicated
  diff-specific keys), never hard-coded, so both variants stay legible.
- A diff can be enormous: rendering is virtualised per line, and a file over the truncation limit
  shows a "this file is large — show anyway" affordance instead of freezing.
- A merge commit has no single diff: the panel offers "against first parent" (default), "against
  second parent", and the combined diff, and says which is shown.

## PHASE01 — Changed files list/tree panel

**Status:** DONE — see `docs/done/FEATURE-7D1B-PHASE01.md`

**Steps**

1. Core: `IDiffService.GetChangedFilesAsync(sha, DiffTarget, ct)` built on `git diff-tree -r -m
   --name-status -z --find-renames --find-copies`, plus `--numstat` for the per-file line counts, and
   the working-tree variants (`HEAD` vs index vs work tree) for FEATURE-13FE.
2. `ChangedFilesPanelViewModel` — the flat list and the tree built by `FileTreeBuilder`, a persisted
   `ViewMode` toggle (List / Tree), a search box, and totals (files changed, insertions, deletions).
3. `ChangedFilesPanelView` — a `TreeView` bound to the tree and a `ListBox` bound to the flat list,
   swapped by the toggle; each row shows the status glyph (A/M/D/R/C/T, colour-coded), the path (with
   the directory dimmed in list mode), the rename arrow when applicable, and a `+n −m` badge.
4. Per-file context menu: open the file, copy the path, view the file at this revision, and open the
   file's own history.
5. Selecting a file raises the selection the diff viewer observes; the panel restores the previously
   selected path when the commit changes, when it still exists.

**Acceptance criteria**

- Unit tests: the view-mode toggle maps the same `ChangedFile` set onto both shapes; the status glyph
  and colour map covers every `ChangeKind`; search filters both shapes identically; totals are right.
- Integration tests: `GetChangedFilesAsync` returns the expected statuses for a commit containing an
  add, a modify, a delete, a rename with edits, and a mode change; a merge commit returns the diff
  against the requested parent.
- The panel handles a commit touching 10 000 files without stalling (virtualised).

## PHASE02 — Colour-coded diff viewer

**Status:** TODO

**Steps**

1. Core: `IDiffService.GetPatchAsync(sha, path, DiffOptions, ct)` — `git diff` /
   `git show --format= -p` with `--find-renames`, configurable context lines, and the whitespace
   options (`--ignore-all-space`, `--ignore-blank-lines`).
2. `DiffViewerViewModel` — the parsed `FilePatch`, a `DiffViewMode` toggle (**Unified** /
   **Side-by-side**), context-line and whitespace options, a "show whitespace characters" toggle, and
   the flattened, virtualisable row list for each mode.
3. `DiffLineRow` visual: gutter with old and new line numbers, a `+`/`−` sign column, a full-width
   background tinted by kind, and a `TextBlock` built from `Inlines` so word-level segments get a
   stronger highlight inside the line tint.
4. Side-by-side mode aligns removed against added lines, padding with blank filler rows so both
   columns stay in step; the two scroll viewers are synchronised on both axes.
5. Hunk headers render as a subtle band showing the `@@` ranges and the section heading, with
   expandable context (clicking a header loads more surrounding context by re-running git with a
   bigger `-U`).
6. Rendering details: a monospace font (`ui-monospace` stack with an explicit fallback list so Linux
   and Windows both resolve), tab expansion to the configured tab width, no text wrapping by default
   with a toggle, and a horizontal scrollbar shared by both panes.
7. Binary files, submodule changes, symlink changes and empty diffs each render an explicit message
   rather than an empty pane.
8. Actions: copy the selected lines (without the `+`/`−` markers), copy the whole patch, and toggle
   whitespace handling.

**Acceptance criteria**

- Unit tests: unified and side-by-side row projections of the same patch (alignment, fillers, line
  numbers); selected-line copy strips markers; expanding context re-requests with a larger `-U`.
- Headless render test: added, removed, context and word-diff rows resolve distinct brushes in both
  theme variants, and a contrast check asserts the foreground stays readable on each tint.
- Integration test: the viewer renders a real 5 000-line patch in < 500 ms (virtualised), and a file
  beyond the limit shows the truncation affordance instead.
- Zero `AVLN` warnings.

## Out of scope

- Editing or staging individual hunks — FEATURE-13FE covers staging at file level; hunk staging is
  recorded as a follow-up.
- Conflict resolution — FEATURE-6DCC.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| List vs tree | A persisted per-user toggle, tree as the default | The draft asks explicitly for the user's choice; tree is what scales to a large commit | tree only; list only |
| Diff modes | Unified default, side-by-side available | Unified reads better in a narrow pane and is what the commit view usually wants; side-by-side is what people ask for on big rewrites | one mode only |
| Merge commit diff | First parent by default, with an explicit switch | "Diff against first parent" is what every client shows and what reviewers expect; the combined diff is available but confusing as a default | combined only (unreadable); refusing to diff merges |
| Line rendering | `TextBlock` inlines per line inside a virtualised list | Gives word-level highlighting and selection without a custom text engine | a custom text control (weeks of work); `TextEditor`-style controls (heavy dependency) |
| Large files | Truncate with an explicit "show anyway" | Never freeze the UI on a generated file; still allow the user to insist | always render (freezes); refuse (useless) |
