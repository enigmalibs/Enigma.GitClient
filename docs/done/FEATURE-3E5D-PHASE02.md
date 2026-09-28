# FEATURE-3E5D-PHASE02 — Sorting the tags

**Item:** FEATURE-3E5D — Sort branches and tags
**Branch:** `feature/feature-3e5d-phase02-sort-tags`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

The tags dialog gets the same header controls as the branches dialog: a **Sort by** box (Date / Name)
and a direction button with the ascending or descending glyph and a tooltip in words.

- **Default:** by date, newest first. The date is the tagged commit's (`GitTag.TargetDate`), which is
  the date every line shows. A lightweight tag and an annotated one therefore order the same way.
- The name, compared without regard to case and A to Z, breaks ties.
- **Remembered separately from the branches' order** (`tagSortKey`, `tagSortDirection` in
  `settings.json`), with the same normalisation and the same reaction to a reset.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Configuration/AppSettings.cs` — `TagSortKey`, `TagSortDirection`, and
  normalisation.
- `src/Enigma.GitClient.App/ViewModels/Pages/TagsPageViewModel.cs`:
  - takes `ISettingsService`;
  - the sort state and command, as on the branches page;
  - `Rebuild` orders through `RefSort`.
- `src/Enigma.GitClient.App/Views/Pages/TagsPageView.axaml` — the header's sort controls.
- `tests/Enigma.GitClient.App.UnitTests/RefListSortTests.cs` — 6 tests:
  - the default;
  - the chosen orders (theory ×3);
  - remembered apart from the branches;
  - the header controls.
- `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs` — the tag order: its
  default, a round trip, and normalisation.
- `tests/Enigma.GitClient.App.UnitTests/Infrastructure/TestServices.cs` — `OrderListsByName()`.
- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs`, `TagsPageTests.cs` — their open helpers
  pin the name order (see *Deviations*).
- `docs/roadmap.md`, `docs/plan/FEATURE-3E5D.md` — statuses. The item is done.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The tag's date | The tagged commit's date | It is what each line shows, and what `GitTag.TargetDate` says the list sorts on |
| Tests that look lines up among the realised ones | Pinned to the name order through a test helper | Their commits land in the same second or not from run to run, so newest first is not a stable order for them. Those tests are about badges, menus and drags. |

## Deviations & follow-ups

- **A latent flake from PHASE01, fixed here.** The first full run of this dev failed
  `BranchesPageTests.ABranchRow_DrawsWhereItStandsWithItsRemote`. The test looks a row up among the
  containers a 560 px window realised. Under the new date order, a row's position depends on whether the
  test's commits straddle a second, so the row it wanted could be below the fold.
  - Fix (cycle 1): `TestServices.OrderListsByName()`, called from the branches and tags test classes'
    open helpers.
  - Afterwards: two full runs green, and four stress runs of the three classes green.
- Line endings: no CRLF churn.

## Documentation sweep

No README statement is affected. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2428 passed**, 0 failed, twice (7 new), after one fix
  cycle (above).
