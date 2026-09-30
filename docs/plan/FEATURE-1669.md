# FEATURE-1669 — New repositories start with a README

**Status:** DONE — see `docs/done/FEATURE-1669.md`
**Type:** FEATURE
**Branch:** `feature/feature-1669-readme-first-commit`
**Run:** feature/2026-09-30-profile-lists-readme-diffs

## Objective

When a repository is created with the start window's Create button, a `README.md` is written and
committed as the repository's first commit. Its content is the repository's title: `# <REPO_NAME>`.

## Context & constraints

- `RepositoriesPageViewModel.OnCreateAsync` shows `InitRepositoryDialogView` (parent directory,
  directory name, initial branch), calls `IRepositoryService.InitAsync(path, branch)`, opens the
  repository, adds it to the list, reports success and shows the repository window.
- `RepositoryService.InitAsync` (Core) runs `git init .` and `git symbolic-ref HEAD refs/heads/<branch>`,
  then discovers the repository. `RepositoryHandle.Name` is the work tree's directory name.
- The dialog accepts an existing directory that is not already a repository, so the target may
  already hold files, a `README.md` included.
- A commit needs a git identity. On a machine without `user.name` / `user.email`, `git commit` fails
  with a `GitCommandException`.
- Tests: `tests/Enigma.GitClient.Core.IntegrationTests/Repositories/RepositoryServiceTests.cs`
  (real git, with an isolated global config and an identity in the environment) and the App's
  `RepositoriesPageTests` (headless, `ScriptedContentDialogService`).

## Steps

1. Core, `IRepositoryService.CommitReadmeAsync(repository, title, cancellationToken)`:
   - writes `README.md` holding `# <title>` and a newline (UTF-8 without a BOM, LF), unless the file
     already exists, in which case it is kept as it is;
   - `git add -- README.md`, then commits with the message `Initial commit`;
   - returns the commit's SHA.
2. `RepositoriesPageViewModel.OnCreateAsync` calls it after `InitAsync`, with the repository's name:
   - if it fails (`GitCommandException`), the repository is still opened and listed, and a warning
     says the first commit could not be made and why (README.md is left staged);
   - on success the message says the repository is ready with its README as the first commit.
3. Tests:
   - Core integration: a fresh repository gets one commit, `Initial commit`, whose tree is exactly
     `README.md` = `# <name>\n` on the chosen branch; an existing `README.md` is committed unchanged;
     other files in the directory stay untracked;
   - App: Create through the scripted dialog ends on a repository whose HEAD is that commit.

## Acceptance criteria

- A repository created from the start window has exactly one commit, `Initial commit`, containing
  only `README.md` with `# <directory name>`.
- An existing README.md is never overwritten.
- Without a git identity, the repository is still created and opened, and the user is told why there
  is no first commit.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- A README for cloned repositories, or for repositories created any other way.
- Choosing the README's content, a `.gitignore` or a licence in the dialog.
- Committing the directory's other existing files.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The repository's name | The directory name (`RepositoryHandle.Name`) | It is the only name the dialog asks for, and what the list shows | A separate "title" field in the dialog |
| The commit message | `Initial commit` | The convention hosting services use for the same step | `Add README.md`; the README's title |
| Where the logic lives | A Core `IRepositoryService.CommitReadmeAsync`, called by the page after `InitAsync` | Testable against real git. A failed commit is not a failed creation, so the page can tell the two apart | Folding it into `InitAsync` (a failed commit would read as "could not be created"); doing it in the ViewModel |
| An existing README.md | Kept, and committed as it is | Never destroy a user's file. The first commit is still a README | Overwriting it; skipping the commit |
| What is staged | `README.md` only | "A README.md … committed as a first commit"; other files the directory held are the user's to add | `git add -A` |
| No git identity | The repository is created and opened, and a warning explains; README.md stays staged | The repository is real either way, and the next commit from the panel finishes the job | Rolling the repository back; refusing to create it |
| Line ending and encoding | `\n`, UTF-8 without a BOM | Git's own default for new text; a BOM would sit before the `#` | `Environment.NewLine` |
