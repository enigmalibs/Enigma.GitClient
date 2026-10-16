# FEATURE-C3F3-PHASE01 — List, probe and remove in Core

**Item:** FEATURE-C3F3 — Remove worktrees, mark their branches
**Phase:** PHASE01 — List, probe and remove in Core
**Branch:** `feature/feature-c3f3-phase01-worktree-list-remove`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

Core can now list a repository's working trees, count what would stop git removing one, and remove a
linked worktree. The branch the worktree had checked out is always kept.

- **`GitWorktree`** (public sealed record, `src/Enigma.GitClient.Core/Worktrees/`) holds:
  - `Path`: a full path, with the platform's separators and no trailing one;
  - `HeadSha`: empty for a bare entry;
  - `Branch`: the short name, `null` when detached or bare;
  - `IsMain`, `IsBare`, `IsDetached`, `IsLocked`, `LockReason` (empty when none) and `IsMissing`.
- **`WorktreeListParser.Parse(porcelain)`** (public static) reads `git worktree list --porcelain`:
  - one record per blank-line-separated block, the first one the main working tree;
  - `worktree`, `HEAD`, `branch refs/heads/…`, `detached`, `bare`, `locked [reason]` (a quoted
    reason is unquoted) and `prunable [reason]` (sets `IsMissing`);
  - unknown lines are skipped, and so are lines before the first record;
  - CRLF and trailing blank lines are accepted.

  `Normalise(path)` turns git's `/` paths into full platform paths. The parser itself never looks at
  the disk.
- **`IWorktreeService`** grows:
  - `ListAsync(repository)` runs `git worktree list --porcelain` (git 2.7+, so the 2.20 floor reads
    it). It also marks a worktree `IsMissing` when its folder does not exist, because git before 2.31
    prints no `prunable` line.
  - `CountChangesAsync(worktree)` runs `git status --porcelain --ignore-submodules=none` in the
    worktree's folder, the same check `git worktree remove` makes. It returns 0 for a missing folder
    or a bare entry. Ignored files do not count.
  - `RemoveAsync(repository, worktreePath, force)` lists the working trees again, then refuses
    (`GitOperationRefusedException`) four cases, before git runs:
    - a folder that is not one of the repository's worktrees;
    - the main working tree;
    - the working tree the repository is open in;
    - a locked worktree. The message names its reason and `git worktree unlock`.

    Otherwise it runs `git worktree remove [--force] -- <full path>`, and leaves the branch alone.
  - `FindRemovable(repository, worktrees, path)` (public static) holds those refusals. PHASE04's
    Desktop flow can refuse with the same sentences.
  - `BuildRemoveArguments(path, force)` (public static) refuses a blank folder or one that is not a
    full path.

## Files touched

- Created:
  - `src/Enigma.GitClient.Core/Worktrees/GitWorktree.cs`
  - `src/Enigma.GitClient.Core/Worktrees/WorktreeListParser.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Worktrees/WorktreeListParserTests.cs`
  - `tests/Enigma.GitClient.Core.UnitTests/Worktrees/WorktreeRemovalRulesTests.cs`
- Modified:
  - `src/Enigma.GitClient.Core/Worktrees/WorktreeService.cs`: `ListAsync`, `CountChangesAsync`,
    `RemoveAsync`, `FindRemovable` and `BuildRemoveArguments`.
  - `tests/Enigma.GitClient.Core.UnitTests/Worktrees/WorktreeArgumentsTests.cs`: the remove vector.
  - `tests/Enigma.GitClient.Core.IntegrationTests/Worktrees/WorktreeServiceTests.cs`: listing,
    counting and removing against a real repository.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| `GitWorktree`'s shape | A record with `required Path` and `init` properties | Nine values; `ChangedFile` and `CommitRequest` use the same shape. Tests and the parser build it with `with` | A nine-argument positional record |
| Where the refusals live | `FindRemovable`, public static, called by `RemoveAsync` | Core has no `InternalsVisibleTo`. The unit suite tests them without git, and PHASE04 reuses the same sentences | Private, tested only through git |
| A path that is not fully qualified | Refused as "not a worktree of this repository" | It cannot match a listed worktree, and the refusal still comes before git runs | An `ArgumentException` from `RemoveAsync` |
| Comparing folders | Case-insensitive on Windows, ordinal elsewhere | The same rule `WorktreePaths` and `RepositoryHandle` use | Ordinal everywhere |
| A locked reason git quotes | Unquoted with `GitPathQuoting.Unquote` | git 2.31+ C-quotes a reason that holds a line break | Showing the quotes |
| `CountChangesAsync` when git fails | Throws git's error | The folder exists but is not a readable working tree, and the remove flow must say so instead of reporting it clean | Returning 0 |
| A bare entry in `CountChangesAsync` | 0, no git run | It has no working files | Running status in it |

## Deviations & follow-ups

- No deviation from the plan. `FindRemovable` is an addition: the plan only named
  `BuildRemoveArguments` as public.
- `git worktree remove` is passed the folder exactly as Core normalised it. On Windows that is with
  backslashes; git accepted it in every integration test, including a folder that was already gone.

## Documentation sweep

- Nothing changed: this phase has no user-facing effect.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1255 passed, 1 skipped, 0 failed**. This includes 35 new
  tests:
  - the parser: main only; main and linked; detached; bare; locked with a reason, without one, and
    with a quoted one; prunable with and without a reason; `/` paths becoming the platform's; a
    folder with spaces; CRLF and trailing blank lines; no trailing blank line; unknown lines; lines
    before the first record; empty output;
  - the remove vector: with and without `--force`, a folder with spaces, a relative or blank folder
    refused;
  - the refusals: a linked worktree found (also with a trailing separator, and in another case on
    Windows); the main working tree, the open one, a locked one with and without a reason, and a
    folder that is not a worktree, all refused.
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed** (54 s). This includes
  14 new tests:
  - the list: only the main working tree; after an add, the main one first and the new one with its
    branch and sha; the same order from inside the linked one; a detached worktree; a locked one with
    its reason;
  - the count: clean is 0; an ignored file does not count, and the worktree is then removed without
    force;
  - removal:
    - a clean worktree: folder and record gone, branch kept, and `IBranchService.DeleteAsync` then
      succeeds;
    - with a modified and an untracked file: the count is 2, git refuses without force, and force
      removes it;
    - a folder deleted by hand: it is listed as missing, counts 0, and removing clears the record;
    - the main working tree, the open one and a locked one: refused, nothing removed;
    - a folder that is not a worktree: refused.
- Desktop: nothing changed in this phase. The Desktop project compiles.
