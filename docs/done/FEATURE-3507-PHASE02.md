# FEATURE-3507-PHASE02 — Pull, push and delete per branch

**Item:** FEATURE-3507 — Branch actions in the history
**Branch:** `feature/feature-3507-phase02-branch-sync`
**Run:** feature/2026-09-22-home-window-merges-refresh

## Summary

Every local branch badge in the history now offers **Pull "X"** and **Push "X"**, beside the
check-out, merge and delete items it gained in PHASE01. Delete was already there.

- **Pulling the current branch** is the ordinary pull (merge or fast-forward-only, per the setting).
- **Pulling any other branch** is `git fetch <remote> refs/heads/<b>:refs/heads/<local>`, a new
  `ISyncService.FastForwardBranchAsync`. git does that only when it is a fast-forward: HEAD never
  moves and nothing is merged. A branch that has diverged from its upstream is refused, with a sentence
  saying to check it out and pull. The push-oriented message the failure classifier gives a
  non-fast-forward does not apply here, so it is replaced.
- **A branch with no upstream** (or whose upstream is gone) cannot be pulled; the info bar says so and
  suggests "Set upstream" or a first push.
- **Pushing** works for any local branch, current or not: to its upstream's remote, or to `origin`
  with the upstream set when it has none.
- **Remote branch badges** offer no pull or push: there is nothing local to pull into or push from. They
  keep check out (which creates the tracking branch), merge-source and delete.

The refspec is built from full ref names, so neither side can be read as an option. It never starts
with `+`, so it can never be a forced update, and a remote name that starts with a dash is refused.

## Files / modules touched

**Modified — Core**

- `Sync/SyncService.cs` — `FastForwardBranchAsync` and `BuildFastForwardArguments`

**Modified — App**

- `Services/SyncOperations.cs` — `PullBranchAsync`, `PushBranchAsync`, `SplitUpstream`, and an optional
  failure explanation on the shared run
- `ViewModels/Pages/HistoryBranchViewModel.cs` — `Pull` / `Push` commands, `PullHeader`, `PushHeader`,
  `CanSynchronise`
- `ViewModels/Pages/HistoryPageViewModel.cs` — the two handlers (followed by a reload, since both move
  a ref the graph draws); `ISyncOperations` injected
- `Views/Pages/HistoryPageView.axaml` — Pull and Push in the badge menu, local branches only
- `RELEASENOTES.md` — the badge menu's list of actions (sweep)

**Tests**

- `Core.UnitTests/Sync/SyncParsingTests.cs` — the refspec is written in full and never forced; empty
  names and a dash-led remote are refused
- `Core.IntegrationTests/Sync/SyncServiceTests.cs` — a branch that is not checked out is fast-forwarded
  from the remote and HEAD stays put; a diverged one is refused as a non-fast-forward and left where
  it was
- `App.UnitTests/RemotesAndSyncTests.cs` — from the history: pulling a branch that is not checked out
  moves it and not HEAD, and touches no file; one with no upstream says so without starting a
  transfer; the current branch's pull is the ordinary one; pushing a branch that is not checked out
  publishes it and sets its upstream; a remote badge offers no pull or push; `SplitUpstream`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Tags on a branch's fast-forward | `--no-tags` | It updates one branch; tags come with the ordinary fetch |
| The remote of an upstream | The part before the first slash, as git abbreviates it | The upstream's short name is all the ref state carries; remote names with a slash are vanishingly rare, and would fail loudly rather than do something else |
| A diverged branch | Refused, with its own sentence | The classifier's non-fast-forward message is about pushing ("pull first"), which would be the wrong advice here |
| What the page reloads after | Both pull and push | A push moves the remote-tracking branch, which the graph draws |

## Deviations & follow-ups

- `HistoryPageViewModel` now takes 16 dependencies. Moving the branch handlers into a dedicated
  `HistoryBranchActions` service would be the natural split (see the FEATURE-7514-PHASE01 follow-up).
- Recommendation only: line endings were not examined; nothing in this diff showed CRLF churn.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 1897 passed, 0 failed (15 new), first run.
