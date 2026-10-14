# FEATURE-5BF5 — Force push a rejected branch

**Status:** TODO
**Type:** FEATURE
**Branch:** one per phase, see below
**Run:** feature/2026-10-14-home-search-force-push

## Objective

Let the user force push a branch that no longer matches the remote — after an amend, a reset, or
when they mean to replace what is there — behind a warning with a red button that says the action is
dangerous.

## Context & constraints

- **Not implemented in the app today.** Core has `PushRequest.ForceWithLease`
  (`--force-with-lease`, never a bare `--force` — FEATURE-06FE), covered by unit and integration
  tests, but nothing in Desktop sets it; FEATURE-1A7E records "The app has no force-push".
- **Pushes in the app:**
  - the toolbar's Push → `ISyncOperations.PushAsync()` (the current branch);
  - a local branch badge's *Push "x"* in the history → `ISyncOperations.PushBranchAsync(name)`.

  Both check the push guard (`MayPushAsync`) first, then run through `SyncOperations.RunAsync`,
  which reports a `SyncException` as a notification. A rejected push is
  `SyncFailureKind.NonFastForward` ("The remote has commits this push would drop. Pull first, then
  push."); a lease that no longer holds is `StaleLease` ("stale info").
- **The background fetch** (every 60 s by default, and when a repository opens) moves the
  remote-tracking refs. An implicit `--force-with-lease` takes its expected value from those refs,
  so a fetch while the user hesitates silently widens what the push overwrites.
- **House red confirmation:** `IContentDialogService.ConfirmDestructiveAsync(title, message,
  confirmText)` — red confirm button, Cancel is the default.
- **Counting commits:** `IBranchService.CountUnmergedCommitsAsync(repository, branch, into)` runs
  `rev-list --count into..branch`.
- `ISyncOperations` is also implemented by a test double (`ScriptedSync`): the interface is not
  widened.
- Desktop tests are written and compiled, **not run** on this Windows machine; Core suites are run.

## PHASE01 — A lease pinned to a commit

**Status:** TODO
**Branch:** `feature/feature-5bf5-phase01-pinned-lease`

### Steps

1. `PushRequest.ExpectedRemoteTip` (`string?`): the commit the push may replace on the remote.
2. `SyncService.BuildPushArguments`: with `ForceWithLease` and an `ExpectedRemoteTip`, pass
   `--force-with-lease=refs/heads/<Branch>:<sha>` instead of the bare lease. The tip must be a full
   hex object name and the branch must be named; anything else is an `ArgumentException`. A delete
   still never forces.
3. Unit tests: the pinned argument; never a bare `--force`; a malformed tip or a missing branch
   throws; a delete ignores the lease.
4. Integration tests: a pinned lease replaces the remote while it is at that commit; it is refused as
   `StaleLease` when the remote moved — **even after a fetch has moved the remote-tracking ref**, the
   case an implicit lease lets through.

### Acceptance criteria

- `--force-with-lease=refs/heads/<branch>:<sha>` is produced only for a force with a valid tip and a
  named branch; never `--force`/`-f`.
- A pinned force push succeeds when the remote is at the pinned commit and fails with `StaleLease`
  otherwise, whatever the remote-tracking ref says.
- Build clean; Core unit and integration suites green.

## PHASE02 — Force push offered on rejection

**Status:** TODO
**Branch:** `feature/feature-5bf5-phase02-force-push-offer`

### Steps

1. `SyncOperations`:
   - take `IContentDialogService` and `IBranchService`;
   - split `RunAsync` so a push can get its `NonFastForward` failure back instead of a
     notification;
   - `PushAsync` and `PushBranchAsync` — on a non-fast-forward rejection of a named branch, offer a
     force push:
     - the remote-tracking ref `<remote>/<branch>` as last read; when it is missing, or holds
       nothing the local branch lacks (the remote has commits never fetched), no force is offered
       and the rejection says to fetch first;
     - otherwise count the commits only the remote has (`CountUnmergedCommitsAsync(trackingSha,
       into: branch)`) and ask `ConfirmDestructiveAsync`:
       - title `Force push "<branch>"?`;
       - a message naming `<remote>/<branch>`, the commits that will be removed from the remote for
         everyone, that it cannot be undone here, and "to keep them, cancel and pull first";
       - a red **Force push** button;
     - on confirm, push again with `ForceWithLease` and `ExpectedRemoteTip` = the tracking sha the
       question described; success says "Force pushed"; a `StaleLease` is reported as today;
     - on cancel, nothing more is said.
   - `DescribeForcePush(remote, branch, lost)` (internal static, testable) builds the question.
2. Tests (Desktop, compiled): the question's wording and count (1 vs n); a rejected shell push asks,
   and a confirmed one replaces the remote; a declined one leaves the remote alone and says nothing
   more; a branch with no remote-tracking ref is not offered a force; the badge's push does the same.

### Acceptance criteria

- A push rejected because the branch does not match the remote asks, in a red-button dialog, whether
  to force push, naming how many commits the remote loses.
- Confirming replaces the remote branch with the local one through a lease pinned to the commit the
  question described; a remote that moved in between is refused, not overwritten.
- Cancelling changes nothing on the remote.
- No force is ever offered for commits this repository has not fetched.
- Build clean; Core suites green; Desktop tests compile.

## Out of scope

- A bare `--force`, `--force-if-includes`.
- A standalone *Force push* menu item, force pushing tags, force pushing a detached HEAD.
- Listing the subjects of the commits that will be lost.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|----------|--------|-----|-----------------------|
| Already implemented? | No — Core only; the app never offers it | `ForceWithLease` is set nowhere in Desktop; FEATURE-1A7E says so | — |
| When force is offered | When a push is rejected as non-fast-forward, from the toolbar or a branch badge | Force appears exactly when it is needed, and only then — GitKraken's and Fork's flow | A standalone menu item; both |
| Kind of force | `--force-with-lease` pinned to the remote-tracking commit the question described | The background fetch would make an implicit lease cover commits the user was never told about; a bare `--force` is against the house rule | Implicit lease; `--force`; `--force-if-includes` (refuses the common "replace what is there" case) |
| Remote has unfetched commits | No force offered; say to fetch first | Nobody should discard commits they have never seen | Offering it and letting the lease fail |
| The warning | `ConfirmDestructiveAsync`, red **Force push**, Cancel default, the number of commits lost | The house red confirmation; a stray Enter keeps the remote | A plain message box; typing the branch name |
| Cancel | Nothing more is reported | The user chose; the question already said "pull first" | A second "push rejected" notification |
| Phases | Core (pinned lease) then Desktop (the offer) | Two small reviewable commits; Core is independently testable | One dev |
