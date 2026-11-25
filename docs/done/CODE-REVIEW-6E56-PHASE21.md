# CODE-REVIEW-6E56-PHASE21 — [Medium] STYLE-01 Null-forgiving removed

## Summary

Null-forgiving operators (`!`) sat at the review's 34 sites, none saying why it held. Most were in
command lambdas (`row => … row!.Worktree!`) whose null check lived in a separate `CanExecute`. Nothing
tied the two together, and a relay command's `Execute` does not re-check `CanExecute`.

All 67 operators in the review's 26 files are gone: the review's 55, plus 12 more of the same kind in
the same files. A scan of the whole `src` tree now finds no `!` operator at all, so none needed a
stated reason.

| Kind of site | How |
|---|---|
| Command handlers: branches, history, tags, worktrees, submodules, copy | The handler matches its parameter itself, for example `row is { Worktree: { } worktree } ? Open(worktree) : Task.CompletedTask`. A command given no row does nothing |
| Flags that imply a value: a pin conflict's two SHAs, a found repository, a determinate progress, a submodule follow mark | A property pattern over the flag and the values, for example `file is { IsPinConflict: true, OursSha: { } ours, TheirsSha: { } theirs }` |
| `GitExecutable.Path` | `[NotNullWhen(true)]` on `IGitExecutable.TryResolve` and its implementation, so the compiler knows `path` is set when it returns true |
| `ChangedFilesPanelViewModel.CanReachOnDisk` | `[NotNullWhen(true)]` on its parameter |
| Invariants the type does not express: a commit / range / stash diff target's commits, a file node's change, a branch `Refuse` let through | A guard that throws with the invariant in words (`DiffService.Named`, `FileTreeBuilder`, `RevertOperations`) |
| Lists of nullable entries | `OfType<T>()` (`HiddenBranchStore`, `RepositoryListStore`) |
| The exception unwrapping in `UnhandledErrorPolicy.Summarise` | A `Wrapped` switch: an aggregate of exactly one (a list pattern), or an invocation with an inner exception |
| `DiffLineText`'s whitespace brush, `DiffTextEditor`'s rows, `SubmoduleWindowViewModel`'s detached head, `DiffViewerViewModel`'s hunk headers, the history's reset menu entries | The value is captured once by a pattern and passed on |
| `SubmoduleService`, the update's "commit on no branch" check | Made only when a commit is checked out. With none checked out, no commit can be left on no branch |
| `SyncOperations`, the force-push offer | Made with the request's branch when it has one. Only a named branch's refusal is handed over, so the other arm is never reached and returns the push's own result |

## Files/modules touched

- Modified (Core): `Diff/DiffService.cs`, `Files/FileTreeBuilder.cs`, `Git/IGitExecutable.cs`,
  `Git/GitExecutable.cs`, `Merging/ConflictService.cs`, `Submodules/SubmoduleService.cs`, under
  `src/Enigma.GitClient.Core/`
- Modified (Desktop): `Controls/Diff/DiffLineText.cs`, `Controls/Diff/DiffTextEditor.cs`,
  `Services/BranchOperations.cs`, `Services/HiddenBranchStore.cs`, `Services/RepositoryContext.cs`,
  `Services/RepositoryListStore.cs`, `Services/RepositoryOpener.cs`, `Services/RevertOperations.cs`,
  `Services/SubmoduleOperations.cs`, `Services/SyncOperations.cs`, `Services/UnhandledErrorPolicy.cs`,
  `ViewModels/Pages/BranchesPageViewModel.cs`, `ViewModels/Pages/CommitRowViewModel.cs`,
  `ViewModels/Pages/ConflictResolutionPageViewModel.cs`, `ViewModels/Pages/HistoryPageViewModel.cs`,
  `ViewModels/Pages/SubmodulesPageViewModel.cs`, `ViewModels/Pages/TagsPageViewModel.cs`,
  `ViewModels/Pages/WorktreesPageViewModel.cs`, `ViewModels/Panels/ChangedFilesPanelViewModel.cs`,
  `ViewModels/Panels/DiffViewerViewModel.cs`, `ViewModels/SubmoduleWindowViewModel.cs`, under
  `src/Enigma.GitClient.Desktop/`
- Created: `tests/Enigma.GitClient.Desktop.UnitTests/NullCommandParameterTests.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/UnhandledErrorPolicyTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE21.md`

## Deviations & follow-ups

- **67 operators, not 55.** The 12 others were in the same files: the conflict page's pin
  description, the changed-files rename label and tree, the history's copy command. They were
  removed under the same rule. No `!` is left anywhere in `src`.
- **Behaviour changes only where a `!` would have thrown.** A command run with no row now does
  nothing instead of throwing a `NullReferenceException` that only the safety net caught. The
  submodule update no longer passes a missing SHA to git when nothing is checked out. Everywhere
  else the old code could not reach a null, and the new code takes the same path.
- `ConflictResolutionPageViewModel.DescribePinSidesAsync` now takes the path and the two SHAs instead
  of the file. Its one caller matches them out of the pin conflict.
- The force-push fallback in `SyncOperations` returns the push's own result (`false`) in the arm that
  is never reached, rather than throwing in the middle of a push's feedback.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (7 min 33 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
  - A scan for the operator (`!` after an identifier, `)` or `]`, outside comments and strings, not
    `!=`) finds 0 in the 26 files and 0 in all of `src`.
- **Throwaway harness outside the repository**, run on the rewritten `UnhandledErrorPolicy.Summarise`.
  Every check passed:
  - an aggregate of one around an invocation gives the inner message;
  - an invocation around an aggregate of one gives the inner message;
  - an aggregate of two keeps its own message;
  - an invocation with no inner exception keeps its own.
- **New tests (Desktop, compiled, not run):**
  - `NullCommandParameterTests` (3): the branches dialog's row commands, the worktrees and submodules
    row commands, and copy and "select in history" do nothing without a row or with nothing to copy;
  - `UnhandledErrorPolicyTests.AWrapperOfSeveral_SaysItsOwnMessage`.
- The Core changes are covered by the existing Core suites, which run: the diff targets, the file
  tree, the git executable, the pin conflicts, the submodule update.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes these internals.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The extra 12 operators in the listed files | Removed too | The same rule in the same files. A file half-cleaned invites the next `!` | Only the 55 listed |
| Invariants the types do not express | A guard that throws with the invariant in words | A broken invariant fails loudly, where it breaks, saying which | `?? string.Empty` (hides the bug); a `!` with a comment (the plan's last resort) |
| `TryResolve` | `[NotNullWhen(true)]` on the interface and the implementation | The compiler then checks every caller, not just this one | A local null check at the one site |
| A command given no row | Do nothing | The menu or button that sends no row has nothing to act on, and the safety net is not error handling | Throwing `ArgumentNullException` |
