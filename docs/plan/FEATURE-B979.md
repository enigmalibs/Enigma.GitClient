# FEATURE-B979 — Create offers the global identity

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-b979-create-offers-identity`
**Run:** feature/2026-10-15-create-identity-release

## Objective

When a repository is created from the start window (*Create a repository*), the user is asked whether
to write the current global identity's name and email into the new repository's own configuration —
the same question, for the same reasons, as after a clone (FEATURE-6108): the repository then keeps
committing as that identity whatever the global one becomes when another profile is picked.

## Context & constraints

- `ViewModels/Pages/RepositoriesPageViewModel.OnCreateAsync` runs the create dialog, then, inside
  `RunBusyAsync`: `IRepositoryService.InitAsync`, the README first commit (`CommitReadmeAsync`), opening
  the repository, adding it to the profile's list and reporting "Repository created". There is no
  progress overlay on this path.
- `OfferGlobalIdentityAsync(repository)` already asks the clone's question: reads
  `IGitIdentityService.GetGlobalAsync`, asks only for a complete identity, *Use it* / *Not now* (the
  default), writes with `SetLocalAsync`, warns on a failed write. Its failure title and log lines say
  "clone".
- Names and emails are never logged.
- Tests: `RepositoriesPageTests` — `FillInitDialog` (answers every dialog *Primary* and fills the create
  form), `WithAnIdentityOnEveryNewRepository` (the real repository service, with a local identity set
  by real git before the README commit), `ScriptedContentDialogService.Script` (ordered answers),
  `FakeGitIdentityService` (in-memory global and local identities).
- Desktop tests are written and compiled, **not run** on this Windows machine; the Core suites are run.

## Steps

1. `OfferGlobalIdentityAsync` takes what the repository is to the reader — `clone` or `repository` —
   for its failure title ("The {what} has no identity of its own") and its log lines; the clone keeps
   its exact wording.
2. `OnCreateAsync`: once `InitAsync` has made the repository and before the README commit, call
   `OfferGlobalIdentityAsync(repository, "repository")`.
3. The constructor's `identity` parameter doc and the method's summary say "a new clone or repository".
4. Tests in `RepositoriesPageTests`, mirroring the clone's four:
   - a create with a global identity asks *Use your identity in this repository*, *Not now* the
     default, and *Use it* writes it into the new repository (the global identity is not written);
   - *Not now* writes nothing, and the repository is still created and opened;
   - without a complete global identity nothing is asked;
   - a failed write warns "The repository has no identity of its own" and the repository is still
     created, opened and reported.
5. `README.md`, *Where your things are kept*: git's configuration is also written when a new
   repository is given the identity.

## Acceptance criteria

- After *Create a repository*, with a complete global identity, the user is asked the clone's question,
  and the answer is applied; without one, nothing is asked.
- The clone's behaviour and wording are unchanged.
- Build clean with zero warnings; Core suites green; the new Desktop tests compile.

## Out of scope

- Asking on *Open*.
- Choosing another profile's identity for the new repository.
- Asking when a worktree is created (it shares its repository's configuration).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| When to ask | After `git init`, before the README commit | The first commit is then already made with the identity the repository keeps; the question still comes right after the dialog, like the clone's | After the README commit, before opening (same visible effect, but the first commit would read the global identity) |
| Wording and default | The clone's: same title and text, *Use it* / *Not now*, *Not now* the default | "Exactly like the clone, for the same reasons" | A checkbox in the create dialog (the clone deliberately has none) |
| No global identity | No question | Nothing to copy — as for a clone | Asking anyway |
| Failure | Warning "The repository has no identity of its own"; the repository is still committed, opened and listed | The repository itself was made | Treating the create as failed |
| Code shape | One `OfferGlobalIdentityAsync`, told what the repository is | One path for the question; no copy | A second, create-only method |
| Tests | Four, mirroring the clone's | Same behaviour, same proof | Fewer |
