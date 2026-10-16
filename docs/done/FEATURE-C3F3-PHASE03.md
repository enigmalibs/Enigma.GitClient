# FEATURE-C3F3-PHASE03 — Guards on delete and checkout

**Item:** FEATURE-C3F3 — Remove worktrees, mark their branches
**Phase:** PHASE03 — Guards on delete and checkout
**Branch:** `feature/feature-c3f3-phase03-worktree-guards`
**Run:** feature/2026-10-16-feature-c3f3-feature-ca9d-feature-c56e

## Summary

No delete or checkout started from the application reaches git's "used by worktree" error any more.
Both are refused first, in plain words that name the folder. A refused checkout offers to open that
worktree in a new window.

- **Delete** (`BranchOperations.DescribeAsync`): a local branch another working tree has checked out
  gets the skip reason `WorktreeReason(worktree)`, "it is checked out in the worktree at <path>".
  - **Alone:** a warning *Could not delete "x"*: "\"x\" is checked out in the worktree at <path>.
    Remove the worktree first." No question is asked and git does not run. For the main working tree,
    seen from a linked one, the advice is "Switch it to another branch there first.", because the
    main working tree cannot be removed.
  - **In a batch:** the question lists it as "skipped: it is checked out in the worktree at <path>".
    The others are deleted, and the summary names it with that reason.
  - **A batch of only refused branches** (the checked-out one and a worktree's, say) asks nothing.
    One warning names each branch with its reason.
- **Checkout:** the new `IWorktreeGuard` / `WorktreeGuard` service runs before git does.
  - When another working tree has the branch, it raises *Branch in a worktree*: "\"x\" is checked out
    in the worktree at <path>. Open it in a new window?", primary *Open*, close *Cancel*.
  - *Open* calls `IInstanceLauncher.Launch(path)`. A start that fails is reported as *No new window*,
    naming the folder.
  - For a missing folder it shows the message alone: "… Its folder is missing: remove the worktree to
    free the branch.", with only *OK*.
  - The checkout returns `false` whatever the answer.
- **Where the guard runs:**
  - `BranchOperations.CheckoutAsync(name, isRemote)`: for a remote branch, the guard is about its local
    counterpart (`BranchService.LocalNameFor`). That covers the branches dialog's remote rows, the
    history's badges, and a drop-merge that checks its target out first (`BranchDropOperations`).
  - `CheckoutOperations.CheckoutAsync(revision, …)`, unless `detach` is set: that covers the branches
    dialog's local rows and the tags page. A detached checkout of the branch's commit is still allowed,
    because git allows it too.
- `IWorktreeGuard` is registered as a singleton, and `CompositionRootTests` resolves it.

## Files touched

- Created:
  - `src/Enigma.GitClient.Desktop/Services/WorktreeGuard.cs`
  - `tests/Enigma.GitClient.Desktop.UnitTests/WorktreeGuardTests.cs`
- Modified:
  - `src/Enigma.GitClient.Desktop/Services/BranchOperations.cs`: `WorktreeReason`, the skip in
    `DescribeAsync`, `ReportNothingToDelete`, and the guard in `CheckoutAsync`.
  - `src/Enigma.GitClient.Desktop/Services/CheckoutOperations.cs`: the guard.
  - `src/Enigma.GitClient.Desktop/DependencyInjection/ServiceCollectionExtensions.cs`: `IWorktreeGuard`.
  - `tests/Enigma.GitClient.Desktop.UnitTests/CompositionRootTests.cs`: `IWorktreeGuard` resolves.
  - `tests/Enigma.GitClient.Core.UnitTests/Configuration/SettingsServiceTests.cs`: one test now awaits
    the write it starts (see *Deviations*).

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| One guard or two copies | A small `IWorktreeGuard` service that both `BranchOperations` and `CheckoutOperations` call | The same question, asked in the same words wherever a checkout starts. One more dependency on each service, not three (dialogs, launcher, info bar) | The dialog code in both services |
| The guard in `CheckoutOperations` | Before the detach question, and only when `detach` is false | git refuses the branch, not its commit: `checkout --detach topic` works, and refusing it would take away a way out | Guarding every revision |
| A remote branch | Guarded through its local counterpart's name | Checking out `origin/topic` checks out `topic` when it exists, and the "reset local to here" path does too | Checking only local rows |
| The missing folder's dialog | `ShowAsync` with only a close button (*OK*) | "The message alone, no *Open*". `ShowAsync` is what the scripted test double records | `ShowMessageAsync`, which the double does not record |
| Deleting the main working tree's branch from a linked worktree | "Switch it to another branch there first." | The main working tree cannot be removed, so "Remove the worktree first" would be wrong advice | The same sentence for both |
| A batch where every branch is refused | No question; one warning listing each reason | Nothing would be deleted, and the old code reported only the first branch | Asking a question with nothing to delete |

## Deviations & follow-ups

- **Fix cycle 1 of 3 was spent on an existing flaky Core test,** not on this phase's code. Core is
  untouched in this phase.
  - `SettingsServiceTests.ChangingSomethingRaisesTheEventWithTheNewSettings` failed three full-suite
    runs in a row: `IOException` on `settings.json.<guid>.tmp` in the class's `Dispose`. The same test
    passed on its own and in the earlier phases' runs.
  - Cause: with a zero debounce, `Update` starts a fire-and-forget write, and the test returned and
    deleted the folder while that write still had its temporary file open.
  - Fix: the test awaits `FlushAsync` before it ends. The suite then passed 3 runs out of 3.
- **Follow-up (not done):**
  - `SettingsService.Dispose` disposes its write gate even while a fire-and-forget write still holds
    it. That write's `Release` would then throw `ObjectDisposedException` in an unobserved task. It is
    harmless today, because the application flushes before disposing, but it is worth a `BUG` item.
  - The other `SettingsServiceTests` that change a value with a zero debounce have the same latent
    teardown race.
- **Out of the plan's scope, noted:** pulling a non-current branch that another worktree has checked
  out (`fetch origin x:x`) is refused by git with its own message. It is not guarded here.

## Documentation sweep

- Nothing changed. No user-facing document describes deleting or checking out a branch in this much
  detail.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- `Enigma.GitClient.Core.UnitTests`: **1255 passed, 1 skipped, 0 failed**, in 3 runs out of 3 after
  the fix (before it, 1 failed: the flaky test above).
- `Enigma.GitClient.Core.IntegrationTests`: **386 passed, 2 skipped, 0 failed**.
- `Enigma.GitClient.Desktop.UnitTests`: **compiled, not run** (standing instruction: run on Linux
  before tagging). `WorktreeGuardTests` adds 12 tests, against a real worktree:
  - a lone delete is refused with the sentence, asks nothing, and runs nothing;
  - a batch skips the worktree's branch, says why in the question and the summary, and deletes the
    rest;
  - a batch of only refused branches asks nothing and gives each reason;
  - a checkout offers *Open* and launches on the folder; *Cancel* launches nothing; a failed start is
    reported;
  - the checkout of a revision that names the branch is refused, and a detached checkout of it is not;
  - a remote branch whose local branch is in a worktree is refused;
  - a drop-merge onto such a target is refused before anything moves;
  - a missing folder gets the message only;
  - a branch no other worktree has is checked out as before.

  `CompositionRootTests` resolves `IWorktreeGuard`.
- **CRLF:** none of the touched files mixes line endings. Nothing to recommend.
