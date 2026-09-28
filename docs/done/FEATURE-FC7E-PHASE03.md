# FEATURE-FC7E-PHASE03 — Discard from the uncommitted line

**Item:** FEATURE-FC7E — History: commit details, discard all
**Branch:** `feature/feature-fc7e-phase03-discard-uncommitted`
**Run:** bugfix/2026-09-28-changes-commit-details-release

## Summary

The history's *Uncommitted changes* line offers **Discard uncommitted files…** (Trash icon), right
after "Stash all changes…". No other line offers it.

- **The question.** "Discard uncommitted files" — "Throw away every uncommitted change — *N files*,
  staged or not, untracked files included? This cannot be undone."
  - It is asked through FEATURE-CC8E PHASE02's `ConfirmDestructiveAsync`: a red *Discard* button,
    and *Cancel* as the default.
  - *N* counts every path the status names once, whichever half of it changed.
- **What confirming does.**
  - Core: the new `IStagingService.DiscardAllAsync` runs `git reset --hard --quiet HEAD --`, then
    `git clean --force -d --quiet -- .`.
  - The index and the work tree go back to HEAD: a staged rename is undone both ways, staged additions
    go, and untracked files and directories are deleted. Ignored files stay (no `-x`).
  - HEAD does not move.
  - It runs exclusively through `IRepositoryContext`, which re-reads the repository afterwards. The
    history then reloads without the uncommitted line, and the info bar says "Changes discarded".
- **When it is refused.** Before the first commit there is nothing to go back to. While a merge or
  another operation is in progress, discarding would leave a merge that records nothing of the other
  side (*Abandon the merge* is the way out there).
  - `IDiscardOperations.CanDiscardUncommitted` says so, and the menu item is greyed.
  - Called anyway, `DiscardUncommittedAsync` neither asks nor discards.
- **Where it lives.** `DiscardOperations` (App/Services, a singleton) follows `StashOperations`: it
  reads the status, asks, runs the discard exclusively and reports the result.

## Files / modules touched

**Created**

- `src/Enigma.GitClient.App/Services/DiscardOperations.cs` — `IDiscardOperations`, `DiscardOperations`.
- `tests/Enigma.GitClient.App.UnitTests/HistoryDiscardTests.cs` — 5 tests:
  - the entry is on the uncommitted line only, beside the stash, with its icon;
  - the red question names 3 files, and confirming leaves the tree clean and the history without the
    line;
  - cancelling changes nothing;
  - it is refused during a merge;
  - it is refused before the first commit.
- `docs/done/FEATURE-FC7E-PHASE03.md`

**Modified**

- `src/Enigma.GitClient.Core/Staging/StagingService.cs` — `DiscardAllAsync`.
- `src/Enigma.GitClient.App/ViewModels/Pages/CommitRowViewModel.cs` —
  `HistoryRowCommands.DiscardUncommitted`, and the menu entry.
- `src/Enigma.GitClient.App/ViewModels/Pages/HistoryPageViewModel.cs` — the service, the command and
  the reload.
- `src/Enigma.GitClient.App/DependencyInjection/ServiceCollectionExtensions.cs` — the service.
- `tests/Enigma.GitClient.Core.IntegrationTests/Status/WorkingDirectoryServiceTests.cs`:
  - every kind of change goes back, the rename both ways, the ignored file stays, and HEAD does not
    move;
  - it works on a commit with an empty tree.
- `docs/roadmap.md`, `docs/plan/FEATURE-FC7E.md` — statuses; the item is `DONE`.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| `reset --hard HEAD` or `restore --source=HEAD --staged --worktree -- .` | `reset --hard` | Checked in a scratch repository: `restore` fails with "pathspec '.' did not match any file(s) known to git" on a commit with an empty tree, while `reset --hard` succeeds. Both put a rename back. `reset` is already an operation the app runs (`ResetService`). |
| Which ignored files `clean` keeps | All of them: no `-x` | Ignored files are not uncommitted work. The test ignores through `.git/info/exclude`, so that an untracked `.gitignore` is not itself cleaned away. |
| Where the operation lives | `IDiscardOperations`, as the stash has `IStashOperations` | The history has no dialog service of its own. One service asks, runs and reports. |
| The count | Distinct paths across staged and not staged | A file changed in both halves is one file to the reader |

## Deviations & follow-ups

- The plan named `git restore … -- .`. `git reset --hard HEAD` is used instead, because `restore` fails
  on an empty tree (see the decisions above).
- Line endings: no CRLF churn.

## Documentation sweep

The README's working-directory and stash bullets stay true. No edit. The feature goes into the
release notes with FEATURE-10AA.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2490 passed**, 0 failed, 0 skipped (7 new: 2 Core,
  5 App).
- Fix budget: no fix cycle.
