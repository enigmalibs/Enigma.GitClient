# CODE-REVIEW-6E56-PHASE15 — [Medium] PERF-01 Bulk collection refills

## Summary

Bound lists were refilled with `Clear()` and one `Add` per item, so each item cost a
`CollectionChanged` and a layout pass in the bound list. That was a reset plus N events on every
automatic refresh and file-system change: thousands for the tags of an old repository, every page of
the history, and the changed-files tree. The repositories filter paid it on every keystroke.

What changed:

- `RangeObservableCollection<T>` (Desktop, `ViewModels/`, new) derives from
  `ObservableCollection<T>`.
  - `ReplaceWith(items)` refills it and raises one `Reset`.
  - `AddRange(items)` appends and raises one `Add` that carries every item and its starting index.
  - Each raises `Count` and `Item[]` once, and nothing at all when nothing changes. Both read their
    items in full first, so they may be worked out from the collection itself.
- The 14 sites of the review use it, and their properties take its type (a subclass, so every
  binding and test reading them is unchanged):

| Site | Collection | Now |
|---|---|---|
| Tags page | `Tags` | `ReplaceWith` |
| Repositories page, profile picker | `Profiles` | `ReplaceWith` |
| Repositories page, filter | `Repositories` | `ReplaceWith` |
| History page, each page read | `Rows` | `AddRange`, the "uncommitted changes" row included |
| Branches page, manual merge | `MergeSources`, `MergeDestinations` | `ReplaceWith` each |
| Conflict page | `Files` | `ReplaceWith` |
| Profiles page, a profile's integrations | `Integrations` | `ReplaceWith` |
| Profiles page | `Profiles` | `ReplaceWith` |
| Profiles page, earlier integrations | `EarlierIntegrations` | `ReplaceWith` |
| Remotes page | `Remotes` | `ReplaceWith` |
| Submodules dialog | `Rows` | `ReplaceWith` |
| Worktrees dialog | `Rows` | `ReplaceWith` |
| Changed-files panel | `Nodes` | `ReplaceWith` |
| Host repositories dialog, filter | `Repositories` | `ReplaceWith` |

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/RangeObservableCollection.cs`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/RangeObservableCollectionTests.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/TagsPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/BranchesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ConflictResolutionPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/ProfilesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/RemotesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/SubmodulesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/WorktreesPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Panels/ChangedFilesPanelViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Dialogs/HostRepositoriesDialogViewModel.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE15.md`

## Deviations & follow-ups

- **`AddRange` always raises a multi-item `Add`, never a `Reset`.** The plan allowed a `Reset` "when
  the binding cannot take a multi-item add". No such binding exists: every listed collection is bound
  straight to an Avalonia `ListBox`, `ComboBox`, `TreeView` or `ItemsControl`, and the harness confirmed a real
  `ListBox` takes it. Only the history uses `AddRange`. A `Reset` there would re-realise every row
  already loaded at each new page.
- **The line numbers in the review had drifted** (for example `RepositoriesPageViewModel` 1169 and
  1292 are now 1188 and 1311). The sites were found by what they do, and all 14 are covered.
- **The selection lists were left alone** (`SelectedTags`, `SelectedRemotes`). They mirror the list's
  own selection, are filled one by one by the selection sync, and hold a handful of items. They were
  not among the review's sites.
- `using System.Collections.ObjectModel` was removed from the seven files the change left without any
  use of it.
- Other `Clear()`-then-fill patterns outside the review's list (`BranchesPageViewModel.Groups`,
  `ConflictResolutionPageViewModel.Regions`, the history's `Files` and `Diff`) are untouched. They can
  move to `ReplaceWith` in a later pass (Low follow-up).

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (6 min 04 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository**, against the real Avalonia 12.1.4 headless platform
  with Skia and the Fluent theme. Every check passed:
  - `ReplaceWith` of 1,000 items: one `Reset`, the items in order;
  - `AddRange`: one `Add` with its starting index;
  - `ReplaceWith` worked out from the collection's own items;
  - a `ListBox` bound to the collection after a 200-row `AddRange` shows 200 items, its first row
    realised;
  - a second page of 200 keeps the selected row;
  - the last added row realises when scrolled to;
  - a `ReplaceWith` leaves 2 items and clears the selection;
  - a `ComboBox` follows a reset.
- **New tests (Desktop, compiled, not run)** in `RangeObservableCollectionTests` (6):
  - a refill raises one reset, holds the new items in order, and raises `Count` and `Item[]` once;
  - a refill can come from the collection itself;
  - an empty refill of an empty collection says nothing;
  - an empty refill of a full one raises one reset;
  - `AddRange` raises one add of every item at the end;
  - an empty `AddRange` says nothing.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes how lists are filled.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where the collection lives | `ViewModels/`, beside `ViewModelBase`, sealed | Every page and panel ViewModel reaches it without a using | A `Collections/` folder for one type |
| `AddRange` notification | One multi-item `Add` | Avalonia's lists take it (confirmed), and it keeps the rows already realised and the selection | Always a `Reset` (re-realises every loaded row per page) |
| A refill that changes nothing | No notification when both old and new are empty; a reset otherwise | Avoids the empty-to-empty churn on every refresh of an empty list. Comparing contents item by item costs more than the reset it saves | Comparing the sequences first (a cost on every refresh, for rare savings) |
| Property types | Changed to the subclass at every site | A subclass breaks no binding or test, and the call sites need its methods | A private field plus an `ObservableCollection<T>` property (two names for one list) |
