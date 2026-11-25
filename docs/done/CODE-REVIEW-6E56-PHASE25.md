# CODE-REVIEW-6E56-PHASE25 — [Medium] SOLID-02 History row commands

## Summary

The second of the history's splits takes out what a line's own menu does with its commit, and the
stash:

- `HistoryCommitActions` (new, singleton) owns:
  - create a branch here, check the commit out (detached), create a worktree here;
  - reset soft and hard, revert;
  - the squash: set the base, measure a range among the loaded lines, squash;
  - show details.
  It takes the branch, checkout, worktree, reset, revert and squash operations and the commit details
  dialog. `CreateCommands(host, clearSquashBase)` returns a `HistoryCommitCommands` record, with the
  same `HistorySquashCommands` the page exposed as `SquashCommands`.
- `HistoryStashActions` (new, singleton) owns stash, apply, pop, drop and discard-uncommitted. It
  takes the stash and discard operations. `CreateCommands(host, hasUncommittedChanges)` returns the
  `HistoryStashCommands` of the lines, the discard command and the toolbar's `StashCommand`.
- `IHistoryRowHost` extends PHASE24's `IHistoryRefHost` with what a squash needs of the page:
  - the `SquashBase`;
  - the loaded `Rows`, implemented explicitly over the page's own;
  - `RevealAsync(sha)`, to select the squash commit after the reload.
- The handlers moved verbatim, with `ReloadAsync()`, `SquashBase` and `Rows` reached through the host.
  The details panel's own "show details" button runs the same shared command.
- The page keeps every public command property (`RowCommands`, `SquashCommands`, `StashCommand`, …),
  now built from the collaborators. It keeps its state (`SquashBase`, `HasUncommittedChanges`) and the
  commands that are its own (show changes, open on host, copy).

The page's constructor goes from 26 parameters (before PHASE24) to 19, and the file from 2,882 lines
to 2,365.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryCommitActions.cs` (with `IHistoryRowHost`, `HistoryCommitCommands`)
- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryStashActions.cs` (with `HistoryStashActionCommands`)
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE25.md`

## Deviations & follow-ups

- **19 constructor parameters, not "about 12".** Moving every operation the row commands use, and
  the details dialog, removes nine parameters for two. What remains serves the page's own work:
  - the context, the log reader, the working-tree probe;
  - the diffs, the stash list, the remote tags, the hidden branches;
  - the settings, the info bar, the tool dialogs, the host links, the interop;
  - the diff viewer factory, the working-tree panel, the logger;
  - the four collaborators.
  Reaching 12 needs PHASE26's search and stash-list split and more beyond, so the shortfall is
  recorded rather than forced.
- **Wider than the plan's list.** "Create branch here", "checkout" and "show details" moved too. They
  are the same operation-and-reload commands of the same menu, and the last users of the branch and
  checkout operations and the details dialog in the page. The plan named reset, revert, squash and
  worktree-here.
- **The squash base stays the page's state.** The view binds it, and its setter refreshes the lines.
  The actions set it through the host.
- No test changed: tests reach these commands through the page's public command properties.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (5 min 17 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository.** It ran on the headless platform with the real
  container (`ValidateOnBuild`, `ValidateScopes`, a temporary configuration directory, a silent info
  bar, a dialog service that answers "yes"), over a real three-commit repository. Every check passed:
  - the container validates, and the history shows three lines;
  - "Set as squash base" sets the page's base through the host. The range is measured among the
    page's rows, the squash is offered from the top line, and the page's clear command clears the
    base;
  - with uncommitted work the toolbar's stash is enabled. Stashing stashes the work, and the history
    reloads through the host with a stash line and no uncommitted line;
  - "Pop" from the stash line brings the work back;
  - discard is not offered on a commit's line;
  - "Checkout" from a line detaches HEAD at its commit.
- **Public surface:** the page declares the same 80 public members before and after, by name and
  parameters; only the constructor changed.
- **New test (Desktop, compiled, not run)** in `CompositionRootTests`: the commit and stash actions
  are shared singletons; the page takes them and none of the nine dependencies they took over.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the page's structure.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| The host for the commit actions | `IHistoryRowHost : IHistoryRefHost`, with the squash base, the rows and `RevealAsync` | One page implements both; the branch and tag actions keep their narrower view | One wide host interface for all four collaborators |
| The page's `Rows` on the host | An explicit implementation returning `IReadOnlyList<CommitRowViewModel>` | The page's property keeps its type, which bindings and `AddRange` need | Changing the page's property type |
| The details panel's "show details" | Runs the commit actions' `ShowDetails` | One handler for the menu item and the button | Keeping a second copy in the page |
