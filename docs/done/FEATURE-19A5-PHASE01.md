# FEATURE-19A5-PHASE01 — A worktree service

**Item:** FEATURE-19A5 — Create a worktree from a branch or line
**Phase:** PHASE01 — A worktree service
**Branch:** `feature/feature-19a5-phase01-worktree-service`
**Run:** feature/2026-10-15-worktrees-toolbar

## Summary

Core can now create a worktree on a new branch, and says where one should go.

- **`WorktreePaths`** (public static, `src/Enigma.GitClient.Core/Worktrees/`):
  - `DefaultFor(workTreePath, branch)` proposes `<parent>/<folder>.worktree.<branch>`: next to the
    repository, never inside. `ToFolderName` makes one folder of any branch name: `/`, `\`, `<`, `>`,
    `:`, `"`, `|`, `?`, `*` and control characters become `-` (`feature/login` →
    `Repo.worktree.feature-login`). A repository at a file-system root has nothing beside it, so the
    proposal is empty.
  - `Validate(workTreePath, path)` gives the reason a folder cannot take a worktree, or an empty
    string:
    - no folder;
    - not a full path;
    - the repository's own folder;
    - a folder inside it (also through `..`, and case-insensitive on Windows);
    - an existing file;
    - a non-empty directory;
    - one that cannot be read.

    A folder that does not exist yet, or an empty one, is accepted.
- **`IWorktreeService.AddAsync(repository, path, branch, startPoint)`**:
  - refuses (`GitOperationRefusedException`), in this order: an invalid branch name
    (`RefNameValidator`), an existing branch, a folder `Validate` rejects;
  - then runs `git worktree add -b <branch> -- <full path> <startPoint>` (`BuildAddArguments`, public
    static). It refuses a relative folder, a blank argument, and a branch or start point that starts
    with a dash.
- **A failed add leaves no branch behind.** git creates the branch before the folder. Probing showed
  that a folder git then cannot make (under a file, say) leaves the new branch in place, and so does
  git's own "already exists" refusal. When `worktree add` fails, the service deletes the branch if it
  now exists (`branch -D`, not cancellable, failure ignored), then rethrows git's error. The branch
  did not exist before (it was refused otherwise), so nothing is lost.
- Registered as a singleton in Core's DI.

## Files touched

- Created:
  - `src/Enigma.GitClient.Core/Worktrees/WorktreePaths.cs`
  - `src/Enigma.GitClient.Core/Worktrees/WorktreeService.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Worktrees/WorktreePathsTests.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Worktrees/WorktreeArgumentsTests.cs`
  - `tests/Enigma.GitClient.Core.IntegrationTests/Worktrees/WorktreeServiceTests.cs`
- Modified:
  - `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`: `IWorktreeService`.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| A branch left behind by a failed `worktree add` | Deleted with `branch -D` when it exists after the failure | Probing (git 2.52) showed git creates it before the folder; a failure would otherwise leave a stray branch the user never asked for | Leaving it; pre-checking every way the folder could fail |
| The order of the refusals | Name, existing branch, then folder | The dialog reads in that order (the branch field comes first) | Folder first |
| Ending the options | `--` before the folder, plus refusing a dash at the start of the branch or start point | `--end-of-options` needs git 2.24, above the 2.20 minimum; the refusals say why before git does, as `RevertService` does | Only one of the two |
| The rules about what is on disk | Asserted in the integration suite, through the service | Core unit tests touch no file system | Temp folders in the unit tests |
| Reading the current branch in tests | `rev-parse --abbrev-ref HEAD` | `branch --show-current` needs git 2.22, above the 2.20 minimum | `branch --show-current` |
| `Validate` with a path that names a folder under a file | Accepted; git fails and the branch is cleaned up | It is git's failure to report, and it is how the clean-up is tested | Walking every ancestor of the folder |

## Deviations & follow-ups

- No deviation from the plan, apart from the branch clean-up above. The plan did not foresee it; it
  makes the service's "every refusal happens before git runs" guarantee hold in practice.
- Worktree administration files (`.git/worktrees/<name>`) left behind by a failed add, if git leaves
  any, are git's own and `git worktree prune` removes them. Not handled here.

## Documentation sweep

- Nothing changed: this phase has no user-facing effect, and the README's project table still
  describes `Enigma.GitClient.Core` correctly.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1220 passed, 1 skipped, 0 failed**. This includes 37 new tests:
  - the proposed folder: beside, never inside, a trailing separator, slashes, no branch yet, the
    root;
  - `ToFolderName` cases;
  - every rule of `Validate` that needs no disk, including the case-insensitive Windows check, which
    is skipped elsewhere;
  - the argument vector and its refusals.
- `Enigma.GitClient.Core.IntegrationTests`: **372 passed, 2 skipped, 0 failed** (3 min 41 s). This
  includes 11 new tests:
  - created beside the repository on the new branch at the start point, the repository's HEAD
    untouched, the branch in its refs;
  - started from a branch name;
  - a remote branch tracked;
  - an empty folder accepted;
  - an existing branch, an invalid name, a folder inside the repository, a non-empty folder and a
    file refused, with nothing created;
  - a folder git cannot make leaves no branch;
  - an unknown start point creates nothing.
- Desktop: nothing changed in this phase. The Desktop project compiles.
