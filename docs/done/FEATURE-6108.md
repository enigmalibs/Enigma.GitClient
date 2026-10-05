# FEATURE-6108 — Clone offers the global identity

**Item:** FEATURE-6108 — Clone offers the global identity
**Branch:** `feature/feature-6108-clone-offers-identity`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Summary

Once a clone has finished — from the clone dialog or from a host's repository list on the Profiles page,
both of which run `RepositoriesPageViewModel.RunCloneAsync` — and before the clone is opened, the user is
asked:

> **Use your identity in this repository** — Make *Name &lt;email&gt;* the name and email of *clone*?
> They are written to the repository's own configuration, so its commits are made as them whatever the
> global identity becomes later. — *Use it* / *Not now* (the default)

- *Use it* writes git's current global name and email into the clone's own configuration
  (`IGitIdentityService.SetLocalAsync`, i.e. `git config --local user.name / user.email`).
- *Not now* writes nothing.
- No question without a complete global identity: there is nothing to copy.
- A write that fails is a warning info bar ("The clone has no identity of its own"); the clone is still
  opened and listed.
- The clone's progress overlay is taken down before the question, since the overlay is drawn above the
  dialogs.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`: `RunCloneAsync` hides
  the overlay and calls `OfferGlobalIdentityAsync` before opening the clone.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`: four tests.
- `README.md`: when git's configuration is written (and that sentence re-wrapped).
- `docs/roadmap.md`, `docs/plan/FEATURE-6108.md`: statuses.

**Created**

- `docs/done/FEATURE-6108.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The overlay | Hidden right after the clone, before the question; the `finally` hides it only if still shown | The overlay host is drawn above the dialog host: the question would have been under it |
| A failed read of the global identity | No question, logged by kind | Nothing reliable to offer |
| The wording | Names the identity and the repository; says it outlives a later change of the global one | That is the consequence of a local identity the reader should know |

## Deviations & follow-ups

- None from the plan.
- Test gate on this Windows host: as recorded in `docs/done/FEATURE-903B.md`.
- Line endings: no CRLF churn; the touched files are LF.

## Documentation sweep

- `README.md`, *Configuration*: git's configuration is also written when a new clone is given the
  identity; the sentence (made over-long by FEATURE-903B's edit) is re-wrapped.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: 0 warnings, 0 errors.
- New tests, with the verification-only teardown change (their clones have commits): all 4 pass —
  `ACloneOffersTheGlobalIdentity_AndUseItWritesItIntoTheClone`, `NotNow_LeavesTheCloneWithoutAnIdentityOfItsOwn`,
  `WithoutAGlobalIdentity_ACloneAsksNothing`, `AFailedWrite_WarnsAndStillOpensTheClone`.
- `Enigma.GitClient.Core.UnitTests`: 1132 total, 1131 passed, 0 failed, 1 skipped.
- `Enigma.GitClient.Core.IntegrationTests`: 344 total, 341 passed, 1 failed, 2 skipped — the racy
  `RepositoryServiceTests.CloneAsync_ClonesALocalRepositoryAndReportsProgress` (see
  `docs/done/BUG-3D1F.md`); this dev changes no Core code; it passed 5 times out of 5 alone right after.
- Desktop, targeted (`RepositoriesPageTests`, `ProfileIntegrationsTests`, `HostRepositoriesDialogTests`,
  `ProfilesPageTests`, `CompositionRootTests`): 167 total, 159 passed, 0 real failures, 8 teardown
  refusals. With the verification-only teardown change (not committed): 165 passed, 2 teardown
  `IOException`s, 0 real failures.
- Fix budget: 0 cycles used.
