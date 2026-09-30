# FEATURE-1669 — New repositories start with a README

**Item:** FEATURE-1669 — New repositories start with a README
**Branch:** `feature/feature-1669-readme-first-commit`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Summary

A repository created with the start window's Create button now gets a first commit:
`Initial commit`, containing only `README.md`, whose content is `# <repository name>` and a newline.

- Core: `IRepositoryService.CommitReadmeAsync(repository, title)`:
  - writes `README.md` (UTF-8, no BOM, LF) unless one exists; an existing one is kept exactly as it is
    and committed instead;
  - runs `git add -- README.md` and `git commit --message "Initial commit"`, then returns the new
    `HEAD`;
  - stages nothing else, so other files already in the directory stay untracked.
- App: `RepositoriesPageViewModel.OnCreateAsync` calls it after `InitAsync`, with the repository's
  name (the directory name the dialog created).
  - The success message now says the repository is ready "with its README as the first commit".
  - If git refuses the commit, the repository is still opened and listed, and a warning explains.
    With no name and email configured the warning says "git has no name and email to commit with.
    Set them on the Profiles page." Any other refusal is given in git's own words
    (`ProfilesPageViewModel.Describe`). Either way README.md is left staged, "ready to commit from the
    history".

## Files / modules touched

**Modified — Core**

- `Repositories/IRepositoryService.cs` — `CommitReadmeAsync`, documented
- `Repositories/RepositoryService.cs` — the implementation, `InitialCommitMessage`, `ReadmeFileName`

**Modified — App**

- `ViewModels/Pages/RepositoriesPageViewModel.cs` — the first commit in `OnCreateAsync`;
  `CommitReadmeAsync` (the page's wrapper) and `FirstCommitProblem`

**Modified — tests**

- `Core.IntegrationTests/Repositories/RepositoryServiceTests.cs` — the first commit holds only
  `README.md` = `# fresh\n`, on the chosen branch, with another file left untracked and no BOM; an
  existing README (CRLF, hand-written) is committed untouched; an empty title is refused before
  anything is written
- `App.UnitTests/RepositoriesPageTests.cs` — Create through the scripted dialog ends on `Initial
  commit` with the README, opened and listed; a refused first commit still opens and lists the
  repository and warns; `FirstCommitProblem` as a theory. `IdentifiedRepositoryService` wraps the
  real service and gives each new repository a local name and email (or refuses its commit), so the
  test never depends on the identity of whoever runs it
- `App.UnitTests/ProfileIntegrationsTests.cs` — its `IRepositoryService` double implements the new
  member

**Modified — docs**

- `README.md` — the Create part of the start window's line
- `docs/roadmap.md`, `docs/plan/FEATURE-1669.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| How the commit is made | `git commit --message` from `RepositoryService` itself | The message is a fixed two-word string. `CommitService`'s temporary message file exists for long typed messages, and pulling it (and its status check) into the repository service would add a dependency for nothing |
| The wording of a refused commit | A plain sentence for a missing identity; git's own words otherwise | git's "Please tell me who you are" is a dozen lines of commands, and this client sets identities on its Profiles page |
| Which exceptions leave the repository open | `GitCommandException` and `IOException` (writing the README) | Both leave a real repository behind. Anything else is a bug and is left to surface |

## Deviations & follow-ups

- **None from the plan.**
- The commit uses whatever identity git has (global or the new repository's local one, which is
  none yet). The profile picked on the start page does not choose it: the picker chooses a list,
  never an identity (FEATURE-711F).
- Line endings: the README is written with LF; `core.autocrlf` users get git's usual conversion on
  checkout. Recommendation only: nothing to normalise in this repository.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 2600 passed, 0 failed (+8). Green on the first run.
