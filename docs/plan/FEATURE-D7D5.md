# FEATURE-D7D5 — Stacked ref badges in the History

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-11-05-history-badges-release

## Objective

On the History page, a line's branch and tag badges are stacked one under the other instead of side
by side. A line with several badges grows taller; every other line keeps the row height of the
preferences. On a taller line, the rest of the line stays at its top: the commit's node, the subject,
the author, the date and the sha are drawn in the line's first band, where the first badge is, not
centred over the whole height. The badges are ordered: the checked-out branch first, then the local
branches, then the remote branches, each by name, and the tags last.

## Context & constraints

- **Where the badges are drawn:** `Views/Pages/HistoryPageView.axaml`, the row template.
  - The row is a `Grid` with `Height="{… RowHeight}"` and six columns.
  - The badges are the `RefStrip` `ItemsControl` (column 1). Its `ItemsPanel` is a horizontal
    `StackPanel` (`Spacing="4"`) and it has `VerticalAlignment="Center"` and `ClipToBounds="True"`.
  - Columns 2–5 (squash flag + subject, author, date, sha) are `VerticalAlignment="Center"`.
- **The row height** is `HistoryPageViewModel.RowHeight`, a preference clamped to 18–48 (36 by
  default). Nothing else in the page reads it: the `ListBox` virtualises with a
  `VirtualizingStackPanel`, which takes rows of different heights.
- **The graph cell** (`Controls/Graph/CommitGraphCell.cs`) draws its node at `middle = height / 2`,
  the edges into and out of it relative to that middle, and straight lanes over the whole height.
  It is stretched to the row's height.
- **The badge order** comes from Core's `RefDecorationIndex.CompareForDisplay`: current branch, local,
  remote, tags, stash, other, each by name (`OrdinalIgnoreCase`). The Desktop's
  `CommitRowViewModel.Project` then adds the stash badge first, and drops a remote branch that is drawn
  in its local branch's badge. Tags are therefore not always last (a note or an unknown ref comes after
  them), and the order depends on what the caller passes in.
- **The Refs column width** is seeded by `RefBadgeMetrics.Measure(row.Refs)`, the sum of a row's
  badges plus the gaps between them, capped at `MaximumRefColumnWidth` (280). A test reads the badge's
  numbers back off a realised badge.
- **Tests on this machine (Linux):** the Desktop suite runs headless; the Core suites run.

## PHASE01 — Badge order: current, branches, tags

**Branch:** `feature/feature-d7d5-phase01-badge-order`
**Status:** TODO

### Steps

1. `ViewModels/Pages/CommitRowViewModel.cs`: after `Project` has built the badge list, sort it with a
   stable order:
   - the checked-out branch;
   - the local branches (a branch drawn with its upstream counts as local);
   - the remote branches;
   - everything that is neither a branch nor a tag (the stash, a note, any other ref);
   - the tags.

   Each group is ordered by name, `OrdinalIgnoreCase`, as Core does. The sort is a small static
   comparer (`RefBadgeOrder`) beside `RefBadgeItem`, so it is tested on its own.
2. Core's `RefDecorationIndex` is left as it is: the Desktop owns how its badges are drawn, and the
   stash badge is only added there.
3. Tests (`tests/Enigma.GitClient.Desktop.UnitTests/HistoryBadgeOrderTests.cs`):
   - the comparer, on a shuffled list: current, local by name, remote by name, other, tags by name;
   - a row built from refs in a deliberately wrong order draws its badges in that order;
   - against real git: a line with `main` (current), `topic`, `origin/zeta`, `v1` and `a-tag` draws
     `main`, `topic`, `origin/zeta`, `a-tag`, `v1`.

### Acceptance criteria

- On every line, the checked-out branch is first, then the local branches, then the remote ones,
  each by name, and the tags are last, by name.
- The build has zero warnings; the whole suite is green against the baseline.

## PHASE02 — Badges stacked, the line on top

**Branch:** `feature/feature-d7d5-phase02-stacked-badges`
**Status:** TODO

### Steps

1. `Controls/BadgeStackPanel.cs` (new), a `Panel` with `LineHeight` and `Spacing` styled
   properties:
   - the first child is centred in the band `[0, LineHeight]`;
   - every next child goes under the previous one, `Spacing` apart;
   - the panel's height is the badges' height plus the same padding below as above the first one.
     With one badge it is exactly `LineHeight`, even when the badge is taller than the band (clipped,
     as today), so a one-badge line does not grow.
   - With no child, its height is 0.
2. `HistoryPageView.axaml`, the row template:
   - the row `Grid`: `Height` becomes `MinHeight`;
   - `RefStrip`: `VerticalAlignment="Top"`, and its `ItemsPanel` is a `BadgeStackPanel` with
     `LineHeight` bound to `RowHeight` and `Spacing="4"`;
   - columns 2–5 sit in a top band: each cell is wrapped in a `Panel` with `Height` = `RowHeight` and
     `VerticalAlignment="Top"`, its content still centred inside;
   - the graph cell gets `LineHeight` = `RowHeight` and stays stretched.
3. `CommitGraphCell`: a new `LineHeight` styled property (default 0 = the whole row, today's
   behaviour; it `AffectsRender`). When it is set and smaller than the row, the node, the incoming and
   the outgoing edges use `middle = LineHeight / 2`, and the node's radius is fitted to `LineHeight`.
   The straight lanes, and the vertical leg of an outgoing edge, still run to the row's bottom, so the
   lanes stay unbroken through a taller line.
4. `RefBadgeMetrics.Measure`: the strip is as wide as its widest badge, not the sum, since the badges
   are no longer side by side. Its remarks and the `RefColumnWidth` remarks say so.
5. Tests:
   - `BadgeStackPanel`: one badge → `LineHeight`; three badges → stacked, the first centred in the
     band, the panel taller than `LineHeight`; no badge → 0;
   - `CommitGraphCell`: with `LineHeight` set on a taller cell, the node is drawn at `LineHeight / 2`
     (the existing test helpers read the drawing back);
   - headless, against real git: a line with three badges is taller than `RowHeight` and draws them
     one under another (same x, increasing y); a line with one badge, and one with none, are exactly
     `RowHeight`; on the tall line, the subject's centre is in the top band;
   - `RefBadgeMetrics.Measure` returns the widest badge; the existing metrics tests are updated.
6. Documentation sweep (§7): the README's History lines about badges, if now wrong.

### Acceptance criteria

- Badges are stacked vertically. A line with several badges is taller; a line with one or none keeps
  `RowHeight`.
- On a tall line, the node, subject, author, date and sha are in the top band, level with the first
  badge; the graph's lanes are unbroken through the line.
- The Refs column is seeded with the widest badge.
- Drag-and-drop of a branch badge onto another still offers the merge.
- The build has zero warnings; the whole suite is green against the baseline.

## Out of scope

- A cap on how many badges a line stacks (every badge is drawn; the line grows).
- Changing Core's `RefDecorationIndex` order.
- Stacking badges anywhere but the History page.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the order is enforced | A Desktop comparer after `Project` | The stash badge and the upstream pairing are Desktop-side; the order then holds whatever the caller passes in | Changing Core's `GroupOrder`: tags would still not be last after the stash badge, which is added later |
| Where "other refs" go | Between the remote branches and the tags | The request says tags are last; the stash is alone on its line in practice | Last, after the tags: contradicts "the tag must appear last" |
| How the tall line is laid out | A `BadgeStackPanel` that centres the first badge in a `RowHeight` band | A one-badge line keeps exactly `RowHeight`, at every row height the preferences allow; no badge-height constant to drift | A `StackPanel` with a computed top margin: needs a badge-height constant; Grid rows with `Auto`/`*`: a spanning strip's extra height can be shared into the top row and push the subject down |
| "The rest of the line on the top" | The node and the text cells in the first `RowHeight` band | That is where the first badge is, so the line reads across | Top edge of the row without a band: the subject would no longer be level with the first badge |
| Graph lanes on a tall line | Straight lanes and the outgoing leg run the whole row | The graph must stay unbroken across rows | Stretching the whole cell's geometry: the node would move down |
| Refs column width | The widest badge | Stacked badges never sit side by side; the old sum would waste width | Keeping the sum |
| Breakdown | Two phases: order, then layout | The order is a self-contained review; the layout touches the view, the graph and the metrics | One phase |
| Tests | Headless Desktop tests, run on this Linux machine | The suite runs here | Compile only |
