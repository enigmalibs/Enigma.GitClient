# FEATURE-1DC2 — New repositories join at the top

**Item:** FEATURE-1DC2 — New repositories join at the top
**Branch:** `feature/feature-1dc2-new-repos-on-top`
**Run:** feature/2026-10-21-ui-polish-release-650

## Summary

A repository that is not yet in the selected profile's list now joins at the **top** of it, not the
bottom. This covers *Open*, *Create*, *Clone* and `Enigma.GitClient.Desktop <path>`, because all four
go through `RepositoryListStore.AddAsync`.

A repository that is already listed keeps its place and takes its new name, as before. Opening a
repository from the list therefore never reorders the list the user dragged into shape.

The change is one line in `AddAsync`: `entries.Insert(0, entry)` instead of `entries.Add(entry)`. The
file format (`repository-lists.json`, version 1) is unchanged, and existing lists keep their order.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/Services/RepositoryListStore.cs`: the insertion point, and the
  interface's and `AddAsync`'s docs.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`: `AddToListAsync`'s
  doc.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryListStoreTests.cs`:
  - `AddAsync_PutsANewRepositoryAtTheEnd` → `AddAsync_PutsANewRepositoryAtTheTop`;
  - new `AddAsync_OfAListedRepository_KeepsTheOrderTheUserDraggedItInto`;
  - the order-dependent expectations follow the new insertion point (rename, long list, remove,
    move, profiles);
  - the move theory seeds its four entries in reverse, so its data is unchanged.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`:
  `OpeningAListedRepositoryAgain_LeavesItWhereItIs` →
  `OpeningANewRepository_PutsItOnTop_AndAListedOneAgain_LeavesItWhereItIs` (expects
  `second, first`).
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryReorderTests.cs` and `RepositoryFilterTests.cs`:
  the seed helpers add in reverse, so the lists still read `alpha, beta, gamma`.
- `docs/roadmap.md`, `docs/plan/FEATURE-1DC2.md`: statuses.

**Created**

- `docs/done/FEATURE-1DC2.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Fix the seed helpers or their expectations | Seed in reverse (`Enumerable.Reverse(Names)`) | Their tests are about dragging and filtering, not insertion; their data stays readable |
| `Names.Reverse()` | `Enumerable.Reverse(Names)` | With C# 14's first-class spans, `array.Reverse()` binds to the in-place `MemoryExtensions.Reverse` |

## Deviations & follow-ups

- **The Desktop suite is not run** on this machine (standing instruction). The updated and new tests
  are written and compiled. Run them on Linux before tagging.
- Line endings: the touched files are LF; no CRLF churn.

## Build / test evidence

- `dotnet build Enigma.GitClient.slnx`: succeeded, **0 warnings, 0 errors** (AVLN included).
- `Enigma.GitClient.Core.UnitTests`: 1307 total, 1306 passed, 0 failed, 1 skipped (pre-existing).
- `Enigma.GitClient.Core.IntegrationTests`: 388 total, 386 passed, 0 failed, 2 skipped (pre-existing).
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction on this machine).
  - One test added: `AddAsync_OfAListedRepository_KeepsTheOrderTheUserDraggedItInto`.
  - Nine tests updated: eight in the store's tests, one on the repositories page.
  - The seed helpers of the reorder and filter tests updated.

## Documentation sweep

Nothing stale. The README describes the list as "in the order you drag its rows into", which still
holds. The release notes for 6.5.0 (FEATURE-6E21) will announce the change.
