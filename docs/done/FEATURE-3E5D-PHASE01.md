# FEATURE-3E5D-PHASE01 — Sorting the branches

**Item:** FEATURE-3E5D — Sort branches and tags
**Branch:** `feature/feature-3e5d-phase01-sort-branches`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The branches dialog's header now has a **Sort by** box (Date / Name) and a direction button, which shows
the ascending or descending sort glyph. Its tooltip gives the order in words ("Newest first", "Oldest
first", "A to Z", "Z to A") and says that a click reverses it.

- **Default:** by date, newest first. The date is the tip commit's date (`GitBranch.TipDate`).
- The order applies **within each group** (Local, then each remote). The checked-out branch is sorted
  like any other; its "checked out" pill still marks it.
- **Name comparison** ignores case, as the lists always did. The name, A to Z, breaks a tie between two
  equal dates.
- **The choice is remembered** in `settings.json` (`branchSortKey`, `branchSortDirection`). A file written
  before these keys existed reads as the default, with no migration. A value this build does not know
  normalises back to the default. Resetting the preferences re-sorts the open list.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/ViewModels/Pages/RefSort.cs` — `RefSort.Order` and `RefSort.Describe`, which
  the tags page shares in PHASE02.
- `tests/Enigma.GitClient.App.UnitTests/RefListSortTests.cs` — 12 tests:
  - the helper, a theory of the 4 orders;
  - the default in every group;
  - the chosen orders (theory ×3);
  - the direction button;
  - remembered;
  - a reset;
  - the header controls.

**Modified**

- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs` — `RefSortKey`, `SortDirection`,
  `BranchSortKey`, `BranchSortDirection`, and normalisation.
- `src/Enigma.GitClient.App/ViewModels/Pages/BranchesPageViewModel.cs`:
  - takes `ISettingsService`;
  - `SortKeys`, `SortKey`, `SortDirection`, `IsSortDescending`, `SortDirectionTip`,
    `ToggleSortDirectionCommand`;
  - `ApplySort`, and sorted groups in `Rebuild`.
- `src/Enigma.GitClient.App/Views/Pages/BranchesPageView.axaml` — the header's sort controls.
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — 3 tests:
  - the default;
  - a round trip over a file without the keys;
  - normalisation.
- `docs/roadmap.md`, `docs/plan/FEATURE-3E5D.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Order of the "Sort by" choices | Date, then Name | The default comes first |
| Settings written back while being applied | No (`_applyingSettings`) | Reading a change must not echo it |
| Redraw on a settings change | Yes, after the page is built | A reset of every preference must show at once |

## Deviations & follow-ups

- None from the plan.
- Line endings: no CRLF churn.

## Documentation sweep

The README does not describe the branches' order, and `settings.json`'s description ("your preferences")
covers the new keys. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2421 passed**, 0 failed (15 new), with no fix cycle.
