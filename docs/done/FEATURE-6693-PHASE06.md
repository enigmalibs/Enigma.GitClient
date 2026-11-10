# FEATURE-6693-PHASE06 — Push guard for submodule commits

**Item:** FEATURE-6693 — Submodule support
**Branch:** `feature/feature-6693-phase06-push-guard`
**Run:** feature/2026-11-09-feature-6693

## Summary

A push can no longer publish a pin whose commit is on no remote of its submodule.

- **Core:** every push but a delete — the forced one and the tag push included — passes
  `--recurse-submodules=check`, which overrides a user's `push.recurseSubmodules`. git's refusal ("The
  following submodule paths contain changes that can not be found on any remote:" and the paths) maps
  to `SyncFailureKind.UnpushedSubmodule`, with `SyncFailure.SubmodulePaths`; it counts as recoverable
  locally.
- **Desktop (`SyncOperations`):** the refusal is handed over, like a non-fast-forward, to an offer that
  names each submodule with its branch and remote — "Push libs/Common, then this repository". Accepted:
  every submodule's profile is asked first (`IPushGuard`, its own repository and remote — one refusal
  stops everything), then each submodule's branch is pushed (upstream set on a first push), then the
  repository again (still offered a force when the remote moved). A submodule on no branch is named
  instead, with *Open <path>*.
- README *Features*: a bullet for the push guard.

## Files/modules touched

- Created:
  - `tests/Enigma.GitClient.Core.IntegrationTests/Submodules/SubmodulePushTests.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/SubmodulePushOfferTests.cs`
  - `docs/done/FEATURE-6693-PHASE06.md`
- Modified:
  - `src/Enigma.GitClient.Core/Sync/SyncFailure.cs`, `SyncService.cs`
  - `src/Enigma.GitClient.Desktop/Services/SyncOperations.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Sync/SyncParsingTests.cs`
  - `README.md`; `docs/roadmap.md`, `docs/plan/FEATURE-6693.md`: statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The tag push | Carries the check too | "Every push except a delete": a tag can point at a commit recording an unpushed pin |
| Where the check is mapped | Before the network failures | git checks the submodules before it contacts the remote |
| Profiles of several submodules | All asked before anything is pushed | A refusal halfway would leave some submodules pushed and the repository not |
| The submodule's remote | Its branch's upstream remote, else `origin` | What the repository's own push does for its branch |
| New dependencies of `SyncOperations` | Optional last constructor parameters (locator, ref reader, launcher) | DI injects them; without them the refusal is reported as before |

## Deviations & follow-ups

- The tag push's argument vector changed (`SyncParsingTests` updated to the new vector).
- Pre-existing, unchanged: the Windows-only `TokenStoreTests` failure (see the PHASE01 record).

## Build/test evidence

- **Baseline:** see `docs/done/FEATURE-6693-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
  - Core unit: 1501 run, 1498 passed, 1 failed (the baseline's), 2 skipped.
  - Core integration: 447 run, 445 passed, 0 failed, 2 skipped.
  - Desktop: **compiled, not run** (this machine's standing rule): `SubmodulePushOfferTests` (4).
- **New tests:** Core unit — the check on every push but a delete (forced and upstream included); the
  refusal mapped over a captured transcript, with both paths (one with a space); another refusal reads
  no paths. Core integration — a parent push recording an unpushed gamma commit fails with
  `UnpushedSubmodule` and `libs/gamma`, and goes through once gamma's branch is pushed. Desktop — the
  offer's words for one and several submodules; accepted, the submodule is pushed before the
  repository; declined, nothing more; a submodule whose profile may not push refuses the whole thing.
- **Fix budget:** 0 cycles.

## Documentation sweep

- README *Features*: the new bullet.
