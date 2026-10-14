# FEATURE-5BF5-PHASE02 — Force push offered on rejection

**Item:** FEATURE-5BF5 — Force push a rejected branch
**Phase:** PHASE02 — Force push offered on rejection
**Branch:** `feature/feature-5bf5-phase02-force-push-offer`
**Run:** feature/2026-10-14-home-search-force-push

## Summary

A push the remote refuses because the branch no longer matches it now offers a force push, behind a
red warning.

- **Where.** Both pushes of a branch offer it:
  - the toolbar's *Push* (`ISyncOperations.PushAsync`);
  - a local branch badge's *Push "x"* in the history (`PushBranchAsync`).

  The push guard still runs first. The interface is unchanged.
- **When.** Only for a `NonFastForward` refusal of a named branch. `RunAsync` was split into
  `RunCoreAsync`, which can hand that one failure back instead of reporting it. A detached HEAD, and
  every other failure, are reported as before.
- **What the user is told.** The remote-tracking branch `<remote>/<branch>`, as last read, is
  compared with the local branch (`IBranchService.CountUnmergedCommitsAsync`).
  - **When it has commits the branch lacks**, `ConfirmDestructiveAsync` asks — red **Force push**
    button, Cancel the default:
    - *Force push "main"?*
    - *"origin/main" has 3 commits that "main" does not have: the branch was rewritten here, or
      someone else pushed to it.*
    - *Force pushing replaces "origin/main" with "main" as it is here — those 3 commits are removed
      from the remote for everyone who uses it. This cannot be undone from here.*
    - *To keep them, cancel and pull first.*

    The singular reads *a commit … that commit is … To keep it*. `DescribeForcePush` builds the text.
  - **When there is no remote-tracking branch, or it has nothing the branch lacks** — the remote's
    new commits were never fetched — no force is offered. *Pushing failed: The remote has commits
    this repository has not fetched, so "main" was not pushed. Fetch, look at what arrived, then push
    again.*
- **Confirmed.**
  - The push runs again with `ForceWithLease` and `ExpectedRemoteTip` set to the remote-tracking
    commit the question described (PHASE01's pinned lease), behind *Force pushing main*, and
    succeeds as *Force pushed*.
  - A remote that moved in between is refused, not overwritten: `StaleLease`, reported as before.
- **Cancelled.** Nothing more is said, and nothing reaches the remote.

## Files touched

- Modified:
  - `src/Enigma.GitClient.Desktop/Services/SyncOperations.cs`:
    - takes `IBranchService` and `IContentDialogService`;
    - adds `ForcePushConfirmText`, `DescribeForcePush`, `PushOrOfferForceAsync`,
      `OfferForcePushAsync`, `DescribeRemoteSideAsync` and `RunCoreAsync`.
  - `tests/Enigma.GitClient.Desktop.UnitTests/RemotesAndSyncTests.cs`: new tests (below), and the
    rejected-push message test expects the fetch advice.
  - `README.md`: the *Remotes* bullet (documentation sweep).

## Decisions taken at build time

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| How a push gets its refusal back | `RunCoreAsync` with a `handOver` predicate, `RunAsync` a thin wrapper over it | Every other caller keeps its behaviour and its signature; one exception filter decides | Catching the exception outside (the overlay and notification would already be done); duplicating `RunAsync` |
| Which remote ref the question reads | `refs/remotes/<remote>/<branch>`, the ref of the push's own destination | `git push <remote> <branch>` writes `refs/heads/<branch>` there, which PHASE01's lease names too | The upstream's name (differs only for a branch tracking a renamed remote branch) |
| What a fresh read of the refs would add | Not done: the refs as last read | A remote-tracking branch older than the remote is caught by the pinned lease, refused as `StaleLease` with "Fetch, look at what arrived" | Refreshing the refs before asking (a write lock for a read that changes nothing for safety) |
| Logging the refusal handed over | `LogInformation` | It is an expected step of a flow the user takes next, not a failure | `LogWarning` as for reported failures |
| The interface | Unchanged | `ScriptedSync` and every caller keep compiling; force is part of a push, not a separate command | A `ForcePushAsync` member |

## Deviations & follow-ups

- No deviation from the plan.
- **A stale test, found when the Desktop suite was run on Linux:**
  `RemotesAndSyncTests.Shell_SaysWhatToDoWhenAPushIsRejected` expected "Pull first". Its remote has a
  commit this repository never fetched, which now gets no force offer and the advice to fetch, as
  planned. It now expects "Fetch, look at what arrived". Test-only change.
- **Documentation sweep:** `README.md`'s *Remotes* bullet said "push (with `--force-with-lease`)",
  which the app did not do. It now describes the offer, the red warning, the pinned lease and the
  unfetched case.
- **Follow-up:** the release notes will need a line for this and for FEATURE-8970 at the next release
  item.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Release`: **0 warnings, 0 errors**.
- Core suites: `Enigma.GitClient.Core.UnitTests` **1183 passed, 1 skipped, 0 failed**;
  `Enigma.GitClient.Core.IntegrationTests` **361 passed, 2 skipped, 0 failed** (3 min 38 s).
- Desktop tests added to `RemotesAndSyncTests`, **compiled, not run**: the project's standing
  instruction is never to run the Desktop suite on this Windows machine. Run them on Linux before
  tagging.
  - `DescribeForcePush`: 1 commit vs 3.
  - A refused shell push asks in red (the `danger` class is checked as the dialog is shown); confirmed,
    it replaces the remote and says *Force pushed*.
  - Declined, the remote is left alone and nothing more is said.
  - No question over commits never fetched, and a warning that says so.
  - A history badge's refused push offers the force too.
- **Re-run on Linux** when the run was committed, with the stale test above fixed:
  `dotnet build` **0 warnings, 0 errors**; `Enigma.GitClient.Desktop.UnitTests` **1525 passed,
  0 failed**; `Enigma.GitClient.Core.UnitTests` **1183 passed, 1 skipped**;
  `Enigma.GitClient.Core.IntegrationTests` **363 passed**.
  - The first Desktop run had one failure,
    `RepositoriesPageTests.OpeningAListedRepositoryAgain_LeavesItWhereItIs`, which this phase does
    not touch. It passed on two full re-runs and fifteen runs of its class: an intermittent test,
    not a regression of this phase.
