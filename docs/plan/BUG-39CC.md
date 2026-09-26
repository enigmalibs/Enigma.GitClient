# BUG-39CC — Merge fast-forwards instead of merging

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-39cc-merge-no-ff`
**Run:** bugfix/2026-09-26-standard-merge-no-ff

## Objective

A merge started from the branches view's manual merge (**Merge**, not **Merge fast-forward**), or
from the history by setting a merge source and choosing **Merge "X" into "Y"**, fast-forwards the
destination whenever it is simply behind the source — so it records no merge commit, exactly like
the fast-forward item next to it. Every "Merge" the app offers must record a merge commit; the
fast-forward-only items stay the way to move a branch without one.

## Context & constraints

- **Root cause.** Every "Merge" entry point asks for `FastForwardMode.WhenPossible`, which
  `MergeService.BuildArguments` turns into a bare `git merge <source>` — and git's default is to
  fast-forward when it can. Only `FastForwardMode.Never` adds `--no-ff`; Core models and tests it
  (`MergeServiceTests.MergeAsync_CanBeMadeToRecordAMergeCommitAnyway`), but nothing in the UI uses it.
  Git is doing what it was asked; what the app asks for is not what the item promises.
- It was a deliberate choice, now shown wrong in use: FEATURE-6DCC kept git's default
  ("`--no-ff` offered but not forced"), and FEATURE-7514 made the manual **Merge** `WhenPossible` to
  match the drop menu. With "fast-forward only" offered beside it, "Merge" reads as "not a
  fast-forward", and on a branch that is simply behind the two items do the same thing.
- Entry points that say "Merge" (all through `IBranchDropOperations.DropAsync` or
  `IMergeOperations.MergeAsync`):
  - `BranchesPageViewModel.ManualMergeCommand` — manual merge, **Merge** (`WhenPossible`)
  - `BranchesPageViewModel.MergeDropCommand` / `MergeReversedDropCommand` — drop menu (`WhenPossible`)
  - `BranchesPageViewModel.MergeCommand` — row menu "Merge into the current branch" (default)
  - `HistoryPageViewModel` `BranchCommands.MergeInto` — badge/line "Merge "X" into "Y"" (`WhenPossible`)
  - `HistoryPageViewModel` `BranchCommands.MergeIntoCurrent` — "Merge "X" into "<current>"" (default)
- The fast-forward-only items (`ManualFastForwardCommand`, `FastForwardDropCommand`,
  `BranchCommands.FastForwardInto`) pass `FastForwardMode.Only` and are correct.
- `MergeService.Classify` already reads a `--no-ff` merge as `Merged` ("Merge made by …"), a
  conflict as `Conflicted` and a contained source as `AlreadyUpToDate`, so reporting needs no change.
- Pull is a separate operation with its own strategy setting and is not affected.
- **Baseline:** to be measured on the dev branch before any change.

## Decisions

See *Decisions taken autonomously* below.

## Steps

1. `Services/MergeOperations.cs`: `IMergeOperations.MergeAsync` and `MergeOperations.MergeAsync`
   default `fastForward` to `FastForwardMode.Never`, and the docs say why — a merge the user asks for
   records a merge commit; a fast-forward is asked for explicitly.
2. `Services/BranchDropOperations.cs`: same default on `IBranchDropOperations.DropAsync` and
   `BranchDropOperations.DropAsync`.
3. `ViewModels/Pages/BranchesPageViewModel.cs`: `MergeDropCommand`, `MergeReversedDropCommand` and
   `ManualMergeCommand` pass `FastForwardMode.Never`; `OnMergeAsync` passes it explicitly.
4. `ViewModels/Pages/HistoryPageViewModel.cs`: `MergeInto` passes `FastForwardMode.Never`;
   `OnMergeIntoCurrentAsync` passes it explicitly.
5. `Views/Pages/BranchesPageView.axaml`: the manual merge buttons' tooltips say what each does —
   **Merge** always records a merge commit; **Merge fast-forward** only moves the destination and
   refuses when the two have diverged.
6. Core is unchanged: `MergeRequest.FastForward` keeps git's default, because Core mirrors git.
7. Tests:
   - `App.UnitTests/ManualMergeTests.cs`: the theory expects `FastForwardMode.Never` for **Merge**;
     the recording double's default matches the interface.
   - Against a real repository, with the destination simply behind the source, each "Merge" entry
     point records a merge commit (HEAD has two parents): the drop menu's merge
     (`BranchesPageTests`), the row menu's merge into the current branch (`MergeOperationTests`), the
     history's merge-into and merge-into-current (`HistoryMergeSourceTests`, `MergeOperationTests`).
   - The history's fast-forward-only item still moves the branch with no merge commit.
8. Docs: `README.md` and `RELEASENOTES.md` say a merge is always recorded as a merge commit.

## Acceptance criteria

- On a destination that is simply behind the source, the manual **Merge**, the history's
  **Merge "X" into "Y"**, the drop menu's merge and its reverse, and both "merge into the current
  branch" items record a merge commit with two parents.
- The fast-forward-only items still move the branch without a merge commit, and still refuse a
  diverged pair.
- A diverged merge, a conflict and an already-contained source behave and report exactly as before.
- Core's `MergeRequest` default is unchanged.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- A third "fast-forward when possible" item, or a preference choosing the merge style.
- Squash or no-commit merges from the UI.
- Pull, whose strategy is its own setting.
- Renaming the menu items.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Is it a bug? | Yes — a UX bug, not a git bug | Every "Merge" sends a bare `git merge`, so beside "fast-forward only" it does the same thing whenever a fast-forward is possible | "Working as designed" (the item does not do what it says) |
| What "Merge" does | Always `--no-ff` (`FastForwardMode.Never`) | With "fast-forward only" as the other item, the pair then covers both intents: a merge commit, or no merge commit | A third "fast-forward if possible" item (clutters every menu for an intent the two items already cover); a preference (new setting for one behaviour, not asked for); relabelling only (still no way to get a merge commit) |
| Which entry points | Every "Merge" in the app, including the two "merge into the current branch" items | "Merge" must mean one thing everywhere — the principle FEATURE-7514 itself stated | Only the two places the report names (the drop menu and row menu would still fast-forward) |
| Where the meaning lives | App layer: `IMergeOperations` / `IBranchDropOperations` default to `Never`, and the ViewModels pass it explicitly | The App services are "merging as a user performs it"; Core mirrors git and keeps git's default | Changing `MergeRequest`'s default in Core (Core stops mirroring git, and its tests describe git) |
| Labels | Menu headers unchanged; manual merge tooltips rewritten | The headers are already accurate once "Merge" records a commit; "The same, refusing…" is no longer true | Renaming every item to "Merge (merge commit)" (longer menus for no extra information) |
| Catching up with a branch that is simply ahead | The fast-forward-only item (or Pull) | That is exactly what it is for, and it now differs from Merge | Keeping `WhenPossible` for remote-tracking sources (one word, two meanings) |
| Docs | One clause in README and RELEASENOTES | This is the behaviour that surprised the user | No mention (it would surprise the next one too) |
