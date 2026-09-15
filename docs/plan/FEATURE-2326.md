# FEATURE-2326 — Commit graph UI

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** `feature/feature-2326-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

The headline feature: a modern, GitKraken-grade commit graph — crisp anti-aliased lanes with rounded
elbows, coloured nodes, ref badges, and a row carrying author, timestamp and short hash — that stays
smooth on a repository with a hundred thousand commits.

## Context & constraints

- The lane maths comes from `CommitGraphLayout` (FEATURE-6DB0 PHASE01); this item only renders it.
- Virtualisation is non-negotiable: rows live in a virtualising `ListBox`, and the graph cell is a
  lightweight `Control` that draws **only its own row's** segments.
- Colours resolve from the theme through `DynamicResource`, so the graph re-colours on a Dark ↔ Light
  switch without a reload.
- Every visual constant (row height, lane width, node radius, curve radius) is a styled property so
  the look can be tuned without touching the drawing code.

## PHASE01 — Graph row rendering control

**Status:** DONE

**Steps**

1. `CommitGraphCell : Control` with styled properties `Row` (`GraphRow`), `LaneWidth` (default 16),
   `NodeRadius` (5), `MergeNodeRadius` (4), `CurveRadius` (8), `StrokeThickness` (2), `Palette`
   (`IReadOnlyList<IBrush>`), `HighlightBrush`.
2. `Render(DrawingContext)` draws, in order: every `Straight` edge as a vertical line at its lane;
   every `BranchOut`/`MergeIn` edge as a vertical segment plus a quadratic elbow into the target lane
   (the rounded S-curve GitKraken uses, not a diagonal); then the node — a filled circle for a normal
   commit, a ring for a merge, a larger ringed dot for HEAD, and a hollow dot for the uncommitted-
   changes pseudo-row.
3. `MeasureOverride` returns `MaxLane × LaneWidth + padding` so the graph column auto-sizes, with a
   maximum beyond which it scrolls horizontally.
4. Pixel-snapping via `RenderOptions` + half-pixel offsets so 1–2 px lines stay crisp at 100 % scale;
   `RenderOptions.SetEdgeMode(this, EdgeMode.Antialias)`.
5. `GraphPalette` — a resource-backed 10-colour palette per theme variant, chosen for contrast on both
   backgrounds and checked for deuteranopia-safe adjacency.
6. A `RefBadge` control: pill-shaped, coloured by ref kind (HEAD, local branch, remote branch, tag,
   stash), with the Phosphor icon for the kind and an ellipsised label, plus a tooltip carrying the
   full ref name.

**Acceptance criteria**

- Headless Avalonia render tests (no window): a cell built from a known `GraphRow` produces the
  expected geometry counts and bounds; measure returns the expected width for 1, 5 and 20 lanes; a
  merge row draws its elbow to the correct lane.
- Zero `AVLN` warnings.
- A 100-row graph renders in a headless layout pass in < 100 ms.

## PHASE02 — History page & virtualisation

**Status:** TODO

**Steps**

1. `HistoryPageViewModel` — holds `ObservableCollection<CommitRowViewModel>`, the layout carry state,
   the current `CommitLogQuery`, and paging (`LoadMoreCommand` triggered by scroll proximity, 2 000
   commits per page).
2. `HistoryPageView` — a `ListBox` with `VirtualizationMode` on, one `DataTemplate` per row laying out:
   graph cell · ref badges · subject (ellipsised) · author (with a generated monogram avatar) ·
   relative timestamp with an absolute tooltip · 7-character short hash in a monospace face.
3. Column layout uses a shared `Grid` column definition set so every row aligns, with draggable
   splitters between subject/author/date/hash and the widths persisted in settings.
4. Toolbar: ref filter (all refs / current branch / a chosen ref), `--first-parent` toggle, author and
   message search (debounced, re-queries git rather than filtering in memory), and a refresh action.
5. Selection drives `IRepositoryContext.SelectedCommit`, which the details panel (FEATURE-7D1B)
   observes. Multi-select of exactly two commits enables "compare these two".
6. An "uncommitted changes" pseudo-row pinned at the top when the working tree is dirty.
7. Keyboard: ↑/↓ move selection, Home/End jump, Ctrl+F focuses search, Enter opens details; every
   icon-only button carries `AutomationProperties.Name`.
8. Row content is built on a background thread (git call + layout) and marshalled onto the UI thread;
   switching repositories cancels the in-flight load.

**Acceptance criteria**

- Unit tests on `HistoryPageViewModel` with a faked reader: paging appends and never duplicates,
  `HasMore` drives `LoadMoreCommand.CanExecute`, a repository switch cancels and clears, a search
  re-queries with the right `CommitLogQuery`, and the dirty working tree adds exactly one pseudo-row.
- Integration test: a temp repository with 5 000 commits loads its first page and lays out in < 1 s.
- Manual/headless smoke: the graph, badges, author, date and short hash all render on one row.

## Out of scope

- The changed-files panel and the diff viewer (FEATURE-7D1B).
- Context-menu actions on a commit (checkout, branch, tag) — added by FEATURE-478C.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Graph rendering strategy | One lightweight `Control` per row inside a virtualising `ListBox` | Virtualisation is the only way to stay smooth at 100 k commits, and per-row drawing keeps each `Render` trivial | a single `Canvas` for the whole history (no virtualisation, unusable at scale); a `WriteableBitmap` (loses crispness on DPI change and hit-testing) |
| Curve style | Quadratic elbows with a configurable radius | This is what reads as "modern" and matches GitKraken's legibility | straight diagonals (visually noisy where many lanes cross) |
| Timestamp display | Relative with an absolute tooltip | What every modern client does; the absolute value stays one hover away | absolute only (wide column, harder to scan) |
| Search | Re-query git (debounced) | `git log --author/--grep` searches the whole history, not just the loaded page | in-memory filtering of loaded rows (silently incomplete) |
| Page size | 2 000 commits | Fills several screens, keeps the first paint fast, and bounds the layout carry | loading everything (multi-second stall on large repos) |
