# FEATURE-6108 — Clone offers the global identity

**Status:** DONE — see `docs/done/FEATURE-6108.md`
**Type:** FEATURE
**Branch:** `feature/feature-6108-clone-offers-identity`
**Run:** vibe/2026-10-05-profiles-graph-fetch-release

## Objective

After a clone, the user is asked whether to write the current global identity's name and email into the
new repository's own configuration (`git config --local user.name / user.email`).

## Context & constraints

- `ViewModels/Pages/RepositoriesPageViewModel.RunCloneAsync` runs every clone — the clone dialog's and the
  one the Profiles page starts from a host's repository list — then opens the repository, adds it to the
  list and reports "Clone finished".
- `IGitIdentityService.GetGlobalAsync` / `SetLocalAsync(repository, identity)`; `GitIdentity.IsComplete`.
- `IContentDialogService.ShowAsync` for a yes/no question (see `ProfilesPageViewModel`'s confirmations).
- Names and emails are never logged.
- Tests: `RepositoriesPageTests` (clones against a local bare repository), `ProfileIntegrationsTests`,
  `ScriptedContentDialogService`, `FakeGitIdentityService`.

## Steps

1. In `RunCloneAsync`, once the clone has finished and before the repository is opened: read the global
   identity; when it is complete, ask "Use {identity} in {name}?" — this writes the name and email to the
   repository's own configuration — with *Use it* / *Not now*, *Not now* the default.
2. *Use it*: `SetLocalAsync`; a failure is a warning info bar — the clone is kept and opened.
3. No complete global identity: no question.
4. Tests: a clone with a global identity asks and, on *Use it*, writes it locally; on *Not now* writes
   nothing; without a global identity nothing is asked; a failed write warns and still opens the clone.

## Acceptance criteria

- After a clone, with a complete global identity, the user is asked, and the answer is applied.
- Build clean, whole suite green, the new tests among them.

## Out of scope

- Choosing another profile's identity for the clone.
- Asking on create or open.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| When | After the clone succeeds, before opening it | One place for both clone entry points; nothing to ask about a failed clone | A checkbox in the clone dialog (the Profiles page's clone has no dialog) |
| Which identity | git's global identity, read then | "The current global identity"; with FEATURE-903B it is the selected profile's | The selected profile's, read separately |
| No global identity | No question | Nothing to copy | Asking anyway |
| Default answer | *Not now* | A write to the repository's config is opt-in | *Use it* |
| Failure | Warning info bar; clone still opened | The clone itself worked | Treating the clone as failed |
