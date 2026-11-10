# FEATURE-6693-PHASE07 — Pin conflicts on the conflict page

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase07-pin-conflicts`
**Run:** feature/2026-11-09-feature-6693

## Summary

A merge that moves a submodule two ways is resolved by picking one of the two commits.

- **Core (`ConflictService`):** the conflict list reads the unmerged stages once (`ls-files -u -z`); a
  conflict with a mode-`160000` stage is a submodule's — `IsSubmodule`, with `OursSha` / `TheirsSha`
  — and is never read as text (no binary probe, no three-way document). `IsPinConflict` when both
  sides record a commit. `ResolveSubmoduleAsync(path, sha)` writes the stage-0 gitlink
  (`update-index --cacheinfo 160000,<sha>,<path>`) and then `UpdateAsync([path])`, under rule 1: when
  the folder cannot move, the conflict is resolved all the same and the result says the folder stayed.
  `ResolveAllWithAsync` takes the side's commit for a pin conflict and leaves other submodule conflicts.
- **Core (`ISubmoduleService.DescribeCommitAsync`):** one commit of a submodule (title, author, date),
  for the cards.
- **The conflict page:** for a pin conflict, the whole-file card shows "A pin is one commit: to keep
  both, merge them inside the submodule first.", two cards — "Ours: f00ba12 — Fix X" / "Theirs: c0ffee1
  — Add Y" with author and date (the hash alone when the submodule cannot say more) — *Take ours* /
  *Take theirs*, and *Open submodule*. Another submodule conflict (added or removed on one side) says
  to resolve it in a terminal, with no keep buttons.
- README *Features*: a bullet for pin conflicts.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Core.UnitTests/Merging/PinConflictParsingTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/PinConflictTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/PinConflictCardTests.cs`
  - `docs/done/FEATURE-6693-PHASE07.md`
- Modified:
  - `src/Enigma.GitClient.Core/Merging/ConflictService.cs`, `Submodules/SubmoduleService.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/Pages/ConflictResolutionPageViewModel.cs`,
    `Views/Pages/ConflictResolutionPageView.axaml`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeSubmoduleService.cs`
  - `README.md`; `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the cards live | In the page's existing whole-file choice card | A pin conflict is a whole choice between two versions, which that card already is |
| *Resolve all* and a submodule added/removed on one side | Left conflicted, not in the resolved list | The page tells the user to resolve it in a terminal; guessing would risk a wrong `.gitmodules` |
| New dependencies of `ConflictService` and the page | Optional last constructor parameters | DI injects them; tests that build them directly compile unchanged |

## Deviations & follow-ups

- The page's headless test covers the cards' text (`DescribePinSide`); the page's wiring itself is
  compiled, not run. The integration tests cover the conflict listing, the resolution and the merge.
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - Core unit: 1508 run, 1505 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped.
  - Desktop: **compiled, not run** (this machine's standing rule): `PinConflictCardTests` (2).
- **New tests:** Core unit — the unmerged stages with their modes; nothing from what is not one; a pin
  conflict has both commits and no text; a submodule removed on one side is left for a terminal. Core
  integration — two branches move gamma two ways, the merge reports one submodule conflict with both
  commits and no document, taking theirs checks gamma out on it and the merge commits it; resolving all
  with ours records our commit. Desktop — a card with the commit, its author and date; the hash alone.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the new bullet.
