# CODE-REVIEW-6E56-PHASE24 — [Medium] SOLID-02 History ref commands

## Summary

`HistoryPageViewModel` (2,882 lines) built and handled every branch-badge and tag command itself, and
took the operations behind them. The first of the history's splits moves those commands out:

- `HistoryBranchActions` (new, singleton) owns what a branch badge's menu does:
  - checkout, merge and fast-forward into, merge into current;
  - set the merge source;
  - delete, delete with its remote, pull, push, rename;
  - create, open (folder, terminal) and remove the branch's worktree;
  - the drop of one badge on another.
  It takes the branch, worktree, drop and sync operations. `CreateCommands(host, …)` returns the same
  `HistoryBranchCommands` record the page exposed, and `CreateDropCommand(host, mode)` the two drop
  commands.
- `HistoryTagActions` (new, singleton) owns create-a-tag-here, push, delete and delete-remote. It takes
  the tag and sync operations and `IRemoteTags`. `CreateCommands(host)` returns a
  `HistoryTagCommands` record whose four commands the page puts into its `RowCommands`.
- `IHistoryRefHost` is the narrow interface the page implements for them: `MergeSource` and
  `ReloadAsync()`. Both members are the page's existing public ones. The actions are singletons the
  page takes, so they cannot take the page. The page hands itself over when it builds its commands.
- The 16 handlers moved verbatim. The only edits are `ReloadAsync()` to `host.ReloadAsync()`, the merge
  source set through the host, and the current branch passed in rather than read back from
  `BranchCommands`.
- The page's constructor loses `ITagOperations`, `IBranchDropOperations` and `ISyncOperations`, which
  only these commands used, and gains the two collaborators. It keeps `IBranchOperations` and
  `IWorktreeOperations`: the line's own menu still creates a branch or a worktree at a commit, which is
  PHASE25's. The page is 2,615 lines.

## Files/modules touched

- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryBranchActions.cs` (with `IHistoryRefHost`)
- Created: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryTagActions.cs` (with `HistoryTagCommands`)
- Modified: `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`
- Modified: `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`
- Modified: `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`
- Modified: `docs/roadmap.md`, `docs/plan/CODE-REVIEW-6E56.md`
- Created: `docs/done/CODE-REVIEW-6E56-PHASE24.md`

## Deviations & follow-ups

- **The host is handed over at command-building time, not through the collaborators' constructors.**
  The plan says they "read the page's state through a narrow interface the page implements".
  Constructor injection of the page into them would be a cycle, so the page passes `this` once, when
  it builds its commands.
- **The collaborators and the host interface are public.** The page's public constructor takes them,
  and a public signature cannot name an internal type.
- **"Drop" and "merge source" went with the branch actions**, as the plan lists. The page keeps the
  merge source's *state* (the property and its clear command, which the view binds), and the
  actions set it through the host.
- No test changed: they reach these commands through the page's `BranchCommands`, `RowCommands`,
  `MergeDropCommand` and `FastForwardDropCommand`, which are the same objects' types.

## Build/test evidence

- Baseline: see `docs/done/CODE-REVIEW-6E56-PHASE01.md`.
- **This dev:**
  - `dotnet build Enigma.GitClient.slnx --no-incremental`: 0 warnings, 0 errors.
  - Core unit: 1534 run, 1531 passed, 0 failed, 3 skipped.
  - Core integration: 449 run, 447 passed, 0 failed, 2 skipped (5 min 31 s).
  - Desktop: compiled, **not run** (this machine's standing rule).
- **Throwaway harness outside the repository**, on the headless platform with the real container
  (`ValidateOnBuild`, `ValidateScopes`, a temporary configuration directory, a silent info bar) and a
  real repository with `main` and `feature`. Every check passed:
  - the container validates, and both collaborators resolve;
  - the history shows both lines, with a badge for `feature`;
  - "Set as merge source" from that badge sets the page's merge source through the host, and the
    page's own clear command clears it;
  - the current branch comes from the page's repository;
  - "Checkout" from the badge checks `feature` out, and the history reloads through the host: the
    badge is now the current branch;
  - the drop and tag commands are wired and keep their `CanExecute`.
- **Public surface:** the page declares the same 80 public members before and after, by name and
  parameters; only the constructor changed, as planned.
- **New test (Desktop, compiled, not run)** in `CompositionRootTests`: the two collaborators are
  shared singletons; the page takes them, and no longer takes the tag, drop or sync operations.
- **Fix budget:** 0 cycles.

## Documentation sweep

- Nothing stale: no user doc describes the page's structure.

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How the actions reach the page | `CreateCommands(host)`, called by the page with `this` | No DI cycle, and every command is still built once, at the page's construction | An `Attach(host)` setter (mutable singleton); constructor injection (cycle) |
| Concrete classes or interfaces | Concrete singletons | They are the page's collaborators, not seams anyone substitutes; tests go through the page | Two new interfaces with one implementation each |
| The page's remaining operations | Keep branch and worktree operations | The line menu's "create branch here" and "create worktree here" still use them (PHASE25) | Moving them now, ahead of their own phase |
