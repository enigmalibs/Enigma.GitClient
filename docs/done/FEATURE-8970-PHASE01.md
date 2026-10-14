# FEATURE-8970-PHASE01 — Filter the repositories

**Item:** FEATURE-8970 — Home list: filter and drag visuals
**Phase:** PHASE01 — Filter the repositories
**Branch:** `feature/feature-8970-phase01-filter-repositories`
**Run:** feature/2026-10-14-home-search-force-push

## Summary

The start window's list of repositories has a filter box. While it holds text, the list cannot be
reordered.

- **The box** sits above the tiles, aligned with them: *Filter by name or path*, automation name
  *Filter repositories*.
  - It is shown while the profile lists any repository.
  - Escape empties it (`ClearSearchCommand`, the house filter command).
- **The rule** (`RepositoriesPageViewModel.Matches`): a repository stays when its name or its path
  contains the text, ignoring case and the spaces around it. Empty or blank text keeps everything.
- **The list:**
  - The page keeps the profile's whole stored list (`_all`); `Repositories` shows what the filter
    keeps, in the stored order.
  - Every read, move, forget and profile switch re-applies the filter.
  - Clearing the text brings every tile back.
- **States:**
  - `HasRepositories` now counts the whole list, so *No repositories yet* is said only when there
    are none.
  - `HasNoMatch` shows *No repository matches this filter* / *Clear the filter to see every
    repository.*
- **No reordering while filtered** (`IsFiltering`, `CanReorder`):
  - The gesture refuses to start on a press.
  - `MoveAsync` does nothing: an index among the rows shown is not one in the stored order.
  - The grip is hidden, in a fixed 22-pixel column so the tile does not shift sideways.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`: `SearchText`,
    `IsFiltering`, `CanReorder`, `HasNoMatch`, `ClearSearchCommand`, `Matches`, `_all` and
    `ApplyFilter`; `MoveAsync` refuses while filtered.
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml`: the filter strip, the
    no-match empty state, the grip's visibility and fixed column.
  - `src/Enigma.GitClient.Desktop/Views/Pages/RepositoriesPageView.axaml.cs`: no drag while the page
    cannot reorder.
  - `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`: the centred empty state test
    picks the page's own empty state by its title.
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryFilterTests.cs`.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| Keep the full list where | A private `_all` beside `Repositories`; `Repositories` stays the bound collection | No binding or test changes for the unfiltered case; the existing reorder tests read `Repositories` | A `CollectionView` filter (a second data path for one page) |
| Where the "can reorder" check lives in the view | At the press, before a pending drag is recorded | Nothing pending, so no move can ever turn into a drag | Checking at the threshold or at the drop only |
| The filter while switching profile | The text stays, and the new list is filtered by it | The box still shows the text; a list that ignored it would contradict the box | Clearing the text on a switch |

## Deviations & follow-ups

- No deviation from the plan.
- **A stale test, found when the Desktop suite was run on Linux:**
  `RepositoriesPageTests.EmptyPage_CentresItsEmptyStateOnThePage` took the page's only `EmptyState`
  with `Single()`, and the no-match state makes two. It now picks *No repositories yet* by its title.
  Test-only change; the empty state itself is unchanged.
- **Follow-up:** the README's start-window bullet and the release notes do not mention the filter
  yet. Nothing they say became wrong, so the documentation sweep left them for the next release item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors** (AVLN included).
- Core suites (`dotnet test --project … -c Release`): `Enigma.GitClient.Core.UnitTests` **1173 passed,
  1 skipped, 0 failed**; `Enigma.GitClient.Core.IntegrationTests` **359 passed, 2 skipped, 0 failed**
  (3 min 09 s).
- Desktop tests added in `RepositoryFilterTests`, **compiled, not run**: the project's standing
  instruction is never to run the Desktop suite on this Windows machine. Run them on Linux before
  tagging.
  - `Matches` theory, 8 cases.
  - Typing filters, and clearing restores the stored order.
  - `ClearSearchCommand`.
  - The no-match state.
  - Blank text filters nothing.
  - A profile switch keeps the filter.
  - `MoveAsync` refused while filtered.
  - A drag gesture refused while filtered.
  - The grip is hidden and shown again.
  - The box sits above the list.
- **Re-run on Linux** when the run was committed, with the stale test above fixed:
  `dotnet build` **0 warnings, 0 errors**; `Enigma.GitClient.Desktop.UnitTests` **1510 passed,
  0 failed**; `Enigma.GitClient.Core.UnitTests` **1173 passed, 1 skipped**;
  `Enigma.GitClient.Core.IntegrationTests` **361 passed**.
