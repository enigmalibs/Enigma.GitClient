# FEATURE-B979 — Create offers the global identity

**Item:** FEATURE-B979 — Create offers the global identity
**Branch:** `feature/feature-b979-create-offers-identity`
**Run:** feature/2026-10-15-create-identity-release

## Summary

*Create a repository* on the start window now asks the clone's question (FEATURE-6108), once
`git init` has made the repository and before its README is committed:

> **Use your identity in this repository** — Make *Name &lt;email&gt;* the name and email of
> *repository*? They are written to the repository's own configuration, so its commits are made as
> them whatever the global identity becomes later. — *Use it* / *Not now* (the default)

- *Use it* writes git's current global name and email into the new repository's own configuration
  (`IGitIdentityService.SetLocalAsync`), so the first commit is already made as the identity the
  repository keeps.
- *Not now* writes nothing.
- No question without a complete global identity: there is nothing to copy (and the README commit then
  fails as before, with its own warning).
- A write that fails is a warning info bar, "The repository has no identity of its own"; the
  repository is still committed, opened and listed, and "Repository created" is still reported.
- The clone's question, wording and failure title are unchanged: both paths go through the one
  `OfferGlobalIdentityAsync`, which is now told what the repository is to the reader (`clone` or
  `repository`).

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Desktop/ViewModels/Pages/RepositoriesPageViewModel.cs`: `OnCreateAsync` offers
  the identity between `InitAsync` and the README commit; `OfferGlobalIdentityAsync` takes `what` for
  its failure title and its two log lines; the constructor's `identity` doc.
- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoriesPageTests.cs`: a `ReadyToCreate` helper and four
  tests.
- `README.md`: *Where your things are kept* — git's configuration is also written when a new
  repository is given the identity.
- `docs/roadmap.md`, `docs/plan/FEATURE-B979.md`: statuses.

**Created**

- `docs/done/FEATURE-B979.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The parameter's type | A `string` noun (`clone` / `repository`) | It only words a title and a log line; an enum would be a type for two words |
| The log lines | Structured, `{What}` and `{Kind}` | Constant templates (CA2254); still no name or email logged |
| Proving "before the first commit" | The test counts the commits (`git rev-list --all --count`) while the question is on screen: `0` | The order is the decision; the test pins it |
| The existing create tests | Unchanged | Their global identity is empty, so no question is asked and their dialog script still holds |

## Deviations & follow-ups

- None from the plan.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

- `README.md`, *Where your things are kept*: "let a new clone use your identity" → "let a new clone or
  repository use your identity" (paragraph re-wrapped).
- `RELEASENOTES.md` describes past releases; this change goes into FEATURE-6352 (6.3.0). No
  `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests` (Release): **1220 passed, 1 skipped, 0 failed**.
- `Enigma.GitClient.Core.IntegrationTests` (Release): **372 passed, 2 skipped, 0 failed** (3 min 44 s);
  this dev changes no Core code.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run**, under the standing instruction never to
  run the Desktop suite on this Windows machine. The four new tests —
  `ACreateOffersTheGlobalIdentity_AndUseItWritesItIntoTheNewRepository`,
  `NotNow_LeavesTheNewRepositoryWithoutAnIdentityOfItsOwn`, `WithoutAGlobalIdentity_ACreateAsksNothing`,
  `AFailedWrite_WarnsAndStillCreatesTheRepository` — and the clone's four should be run on Linux before
  tagging.
- Fix budget: 0 cycles.
