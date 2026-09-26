# FEATURE-2326-PHASE02 — History page & virtualisation

**Item:** FEATURE-2326 — Commit graph UI
**Branch:** `feature/feature-2326-phase02-history-page`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The headline feature is delivered. The history page reads a repository's commits, lays them out,
and renders a virtualised list whose every row carries the graph, the ref badges, the subject, the
author with a generated monogram, the age of the commit and its seven-character hash. Paging,
searching, scope and first-parent filtering all re-query git rather than filtering what happens to
be loaded, and the selection is published to the shell for the details panel to follow.

With this phase FEATURE-2326 is complete.

## Files / modules touched

**Created — Core**

- `Status/WorkingTreeProbe.cs` — `IWorkingTreeProbe`, answering only "is there anything
  uncommitted?", which is what decides whether the pseudo-row appears

**Created — App**

- `Formatting/RelativeTime.cs` — the relative phrase and the absolute tooltip behind it
- `Controls/AuthorAvatar.cs` + its theme — the generated monogram
- `ViewModels/Pages/CommitRowViewModel.cs` — `RefBadgeItem` and the row, formatted once

**Rewritten**

- `ViewModels/Pages/HistoryPageViewModel.cs` — paging, the layout carry, filters, search, selection
- `Views/Pages/HistoryPageView.axaml` — the toolbar, the virtualised list and the row template

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `src/Enigma.GitClient.App/Themes/Controls.axaml`
- `tests/.../Infrastructure/TestServices.cs` — an opt-in for the real reference reader
- `docs/roadmap.md`, `docs/plan/FEATURE-2326.md`

**Created — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — 27 cases driving the page against a
  repository the test builds with real git, including an off-screen render of the finished page

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The graph column's width | `Auto` column, with the **cell** given one explicit width shared by every row | Found by looking at the render: a `ColumnDefinition.Width` cannot take a plain `double`, and the failed binding silently falls back to star — so the graph column swallowed a third of the window. Binding the cell's width instead keeps every other column on the same horizontal position on every line |
| Ref badges | Flattened into `RefBadgeItem` carrying `IsCurrent` | Also found by looking: only `GitBranch` knows whether it is checked out, so a template bound to `GitRef` drew the current branch like any other. The checked-out branch is the first thing a reader looks for |
| Where formatting happens | Once, when the row is built | A virtualised list re-renders its rows constantly; a converter recomputing a relative timestamp per frame is pure waste |
| Search | Re-queries git, debounced by 300 ms | Filtering the loaded rows would search only the current page and quietly miss everything older — worse than not searching. A test proves it finds a commit that is **not** on the loaded page |
| Avatars | Generated monograms, never fetched | Turning a commit's email into an avatar means sending a hash of it to a third party for every author in the history. A stable colour derived from the name gives the same "scan for one person" benefit with no privacy cost and no network |
| The avatar's hash | A small FNV-style hash written out, not `string.GetHashCode` | `GetHashCode` is randomised per process, so every author would change colour on each restart |
| `HistoryIsComplete` for the layout | Set from `!page.HasMore` | The last page genuinely is the end of the history, so a lane waiting for a parent that will never arrive can be closed instead of drawing a line off the bottom forever |
| A repository swap mid-load | A linked token plus a re-check of the handle after the await | Without it, a slow read against the previous repository appends its rows to the new one's list |
| The uncommitted row | A narrow `IWorkingTreeProbe` now, not the full status reader | The history only needs a yes/no. The full reader is the working-directory item's job and builds on the same `status --porcelain=v2` call |
| `PageSize` | A real public property, not a test-only setter | The plan already has it becoming a user setting; a `SetPageSizeForTesting` would have been a worse version of the same thing |

## Deviations & follow-ups

- **Deviation:** column widths are fixed rather than draggable-and-persisted. Persistence needs the
  settings service (FEATURE-5D77); draggable splitters inside a virtualised `ListBox` row template
  need a shared-size scope, which is its own piece of work. Recorded as a follow-up.
- **Deviation:** "load more" is an explicit button pinned to the bottom rather than triggered by
  scroll proximity. It is predictable, it never fires twice on a fast scroll, and it keeps the
  keyboard path obvious. Scroll-triggered loading is recorded as a possible refinement.
- **Deviation:** multi-select "compare these two commits" is not implemented; it belongs with the
  diff viewer that will do the comparing (FEATURE-7D1B).
- **Follow-up:** the commit context menu (checkout, branch here, tag here) arrives with FEATURE-478C,
  which is where those operations exist.
- **Follow-up:** author search is modelled in `CommitLogQuery` and tested at the reader, but the page
  only exposes message search. A single box that searches both is better UX than two boxes; it needs
  a small query-syntax decision and is recorded for the settings/polish item.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 652  failed: 0  succeeded: 652  skipped: 0
```

28 tests are new in this dev. The Definition-of-Done gate passed on its first run. Three issues were
found and fixed while authoring — the faked reference reader hiding the badges and the HEAD marker,
the graph column's failed `GridLength` binding, and the current branch drawn like any other — the
last two only visible by rendering the page and looking at it. The finished page is written to
`snapshots/history-page.png` beside the test assembly.
