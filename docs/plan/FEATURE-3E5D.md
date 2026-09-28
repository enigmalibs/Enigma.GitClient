# FEATURE-3E5D — Sort branches and tags

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

Let the reader sort the branches dialog's and the tags dialog's lines by name or by date, ascending or
descending. The default is by date, descending (newest first).

## Context & constraints

- `RefReader.GetRefsAsync` sorts local and remote branches by name with the checked-out branch first,
  and tags by name. `BranchesPageViewModel.Rebuild` groups them (Local, then one group per remote);
  `TagsPageViewModel` lists tags flat.
- Dates: `GitBranch.TipDate` (tip committer date); `GitTag.TargetDate` (the tagged commit's date, what
  the tags list shows).
- Preferences live in `AppSettings` (a record, camel-case JSON, string enums, `Normalised()`), saved by
  `ISettingsService.UpdateAsync`/`Update`; a property missing from an older file takes its default, so
  no migration is needed.

## PHASE01 — Sorting the branches

**Branch:** `feature/feature-3e5d-phase01-sort-branches`
**Status:** TODO

### Steps

1. Core `Configuration`: `RefSortKey { Name, Date }`, `SortDirection { Ascending, Descending }`;
   `AppSettings.BranchSortKey = Date`, `BranchSortDirection = Descending`, normalised.
2. App: a small `RefSort` helper that orders by name (ordinal, case-insensitive) or date, with the name
   as tie-breaker.
3. `BranchesPageViewModel`: `SortKeys` choices, `SortKey`, `SortDirection`, a toggle command; each
   group is sorted accordingly (no pinned current branch); the choice is saved to the settings and read
   back on start.
4. `BranchesPageView.axaml`: a "Sort by" `ComboBox` (Name / Date) and a direction toggle button
   (ascending/descending icon and tooltip) in the header.
5. Tests: settings round-trip and normalisation; the four orders on a real repository; the default is
   date descending; the choice survives a new page model.

### Acceptance criteria

- The branch lines can be sorted by name or date, ascending or descending, within each group;
  newest first by default; the choice is remembered.
- Build clean with zero warnings; the whole suite green.

## PHASE02 — Sorting the tags

**Branch:** `feature/feature-3e5d-phase02-sort-tags`
**Status:** TODO

### Steps

1. `AppSettings.TagSortKey = Date`, `TagSortDirection = Descending`, normalised.
2. `TagsPageViewModel`: the same sort controls and behaviour, saved separately.
3. `TagsPageView.axaml`: the same header controls.
4. Tests: the four orders; default date descending; remembered.

### Acceptance criteria

- The tag lines can be sorted by name or date, ascending or descending; newest first by default; the
  choice is remembered independently of the branches'.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Sorting the remotes; sorting the history's badges; a Settings-page entry for the sort.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Controls | A Name/Date `ComboBox` plus a direction toggle | Maps one-to-one to the two choices asked for | One four-entry combo (merges two dimensions); a menu of radio items (harder to discover) |
| Remembered | Yes, in `settings.json`, separately for branches and tags | "Preferences that stick" is how this app treats every view choice | Per session only |
| Current branch | Sorted like the others, not pinned | A chosen sort must mean what it says; the checked-out pill still marks it | Always first |
| Grouping | Sort within each group (Local, each remote) | The groups are the page's structure | One flat list |
| Tie-breaker | Name | A stable order for equal dates | Insertion order |
