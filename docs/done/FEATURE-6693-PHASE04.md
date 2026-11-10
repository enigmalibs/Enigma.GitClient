# FEATURE-6693-PHASE04 — Working inside a submodule

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase04-submodule-window`
**Run:** feature/2026-11-09-feature-6693

## Summary

A window opened on a submodule (from the Submodules dialog, or any other way) says whose it is, and
warns while it is on no branch.

- **Core:** `ISubmoduleService.ReadSuperprojectAsync` — `git rev-parse
  --show-superproject-working-tree`, the superproject's folder or `null`.
- **The mark:** a package glyph after the repository's name in the window's strip (beside the linked
  worktree's tree), tooltip "Submodule libs/Common of MainApp — C:\src\MainApp".
- **The band** (`SubmoduleWindowViewModel`): while a submodule's window is on a detached HEAD — "You're
  on no branch — commits made here are easy to lose." — with *Check out main* when a local branch is at
  HEAD (else *Check out origin/main*, through the existing tracking-branch checkout of
  `IBranchOperations.CheckoutAsync(name, isRemote: true)`), and always *Create a branch…* (the existing
  dialog, at HEAD). It goes once HEAD is on a branch, and never shows for a repository that is not a
  submodule.
- README *Features*: a bullet for the window on a submodule.

## Files/modules touched

- Created:
  - `src/Enigma.GitClient.Desktop/ViewModels/SubmoduleWindowViewModel.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SubmoduleWindowTests.cs`
  - `docs/done/FEATURE-6693-PHASE04.md`
- Modified:
  - `src/Enigma.GitClient.Core/Submodules/SubmoduleService.cs`
  - `src/Enigma.GitClient.Desktop/ViewModels/MainWindowViewModel.cs`, `SubmoduleNoticeViewModel.cs`,
    `Views/MainWindow.axaml`, `DependencyInjection/ServiceCollectionExtensions.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/SubmoduleServiceTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/Infrastructure/FakeSubmoduleService.cs`
  - `README.md`; `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the superproject is read | Once per opened repository, in the window's own ViewModel | It never changes for an open repository; the context's refresh stays as it is |
| A remote branch at HEAD | Offered only when no local one is, never `origin/HEAD` | The plan's order; `origin/HEAD` only names another branch |
| The band's colour | The warning band's, like the sign-in and submodule bands | One look for "something to sort out" |
| A ViewModel built after the repository opened | Reads at construction (this one and PHASE03's band) | A window started on a path opens the repository before the shell's ViewModels exist |

## Deviations & follow-ups

- The band ViewModel of PHASE03 also reads at construction now (same startup-order reason).
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - Core unit: 1473 run, 1470 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 437 run, 435 passed, 0 failed, 2 skipped.
  - Desktop: **compiled, not run** (this machine's standing rule): `SubmoduleWindowTests` (6).
- **New tests:** Core integration — the superproject of a submodule and of a nested one, none for the
  repository. Desktop — the mark's tooltip; the band with a local branch at HEAD (checkout and create
  reach the branch operations), with only a remote one (tracking checkout, `origin/HEAD` skipped),
  with none; nothing for a repository that is not a submodule; the band goes once on a branch.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the new bullet.
