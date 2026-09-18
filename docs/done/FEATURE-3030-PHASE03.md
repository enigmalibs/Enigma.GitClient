# FEATURE-3030-PHASE03 — No dragging from the history badges

**Item:** FEATURE-3030 — History list: columns, menus, search
**Branch:** `feature/feature-3030-phase03-no-badge-drag`
**Run:** feature/2026-09-18-columns-selection-minimap

## Summary

A branch badge in the history is no longer draggable, and the commit list no longer accepts drops.
The gesture moves to the branches page, where the two ends of a merge are rows a reader can see and
select rather than badges scattered down a graph.

Three pieces went: the pointer, drag-over, drag-leave and drop handlers in the view (with
`BadgeAt`, `CanDrop`, `IsDraggable`, `Highlight` and the highlighted-badge field), the page
ViewModel's `DropBranchCommand` / `CanDropBranch` / `Request` / `BranchDrop` and its
`IBranchDropOperations` dependency, and the `RefBadgeItem.DragFormat` nothing transfers any more.
The `RefBadge` theme's `.droptarget` ring went with them — the branches page marks a row, not a
badge.

What did **not** go is `BranchDropOperations`: the checkout-then-merge composition, its policy and
its reporting are what FEATURE-3B62 picks up, and its own tests are untouched. It stays registered
in the composition root for that reason.

## Files / modules touched

**Modified — App**

- `Views/Pages/HistoryPageView.axaml.cs` — the drag section is gone, with the tunnelled pointer
  handler and the three `DragDrop` handlers the constructor added
- `Views/Pages/HistoryPageView.axaml` — `DragDrop.AllowDrop` leaves the list; the badge strip loses
  its "Drag a branch onto another to merge it" tooltip, and its comment says where the gesture went
- `ViewModels/Pages/HistoryPageViewModel.cs` — the `BranchDrop` record, `DropBranchCommand`,
  `OnDropBranchAsync`, `CanDropBranch`, `Request` and `IsBranch` are gone, and the constructor no
  longer takes `IBranchDropOperations`
- `ViewModels/Pages/CommitRowViewModel.cs` — `RefBadgeItem.DragFormat` and the `Avalonia.Input`
  import go with it
- `Themes/Controls.axaml` — the `RefBadge` `.droptarget` style

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/HistoryPageTests.cs` — the "dragging a branch" section is
  replaced by `CommitList_TakesNoDrops`, which asserts the list refuses drops and no badge strip
  advertises the gesture

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-3030.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `BranchDropOperations` | Kept, and kept registered | The branches page needs exactly this two days from now in the same run; deleting and rewriting it would throw away its tests as well as its code |
| The drop policy tests | Deleted here, not moved | `BranchDropTests.CanDrop_AcceptsOnlyWhatCanBeMerged` already states the same policy against the service that owns it; the history's copy tested a wrapper that no longer exists |
| The `.droptarget` style | Removed | Nothing sets the class any more, and the branches page's drop mark goes on a row rather than on a badge — a style kept "for later" would be dead the moment later looks different |
| What replaces the deleted tests | One test of the absence | The five deleted tests asserted a gesture; the honest opposite is that the list refuses drops and nothing advertises one |

## Deviations & follow-ups

- **None from the plan.** All three acceptance criteria are covered.
- The suite is twelve tests smaller (1694 → 1682): five drag tests went, one replaced them, and the
  theory cases went with their theory.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1682  failed: 0  succeeded: 1682  skipped: 0
```

One fix cycle, and it was the expected one: the tests naming the deleted members failed to compile
until the dragging section was replaced.

## Documentation sweep

Scanned `README.md` and `RELEASENOTES.md`. Neither mentions dragging a branch badge in the history —
the README's history bullet is about the graph and its columns. There is no `CLAUDE.md`,
`CHANGELOG.md` or `CONTRIBUTING.md` in the repository. Nothing edited.
