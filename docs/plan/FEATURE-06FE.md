# FEATURE-06FE — Remotes & synchronisation

**Status:** IN PROGRESS
**Type:** FEATURE
**Branch:** `feature/feature-06fe-<phase>-<slug>` (one per phase)
**Run:** feature/2026-09-15-enigma-git-client

## Objective

Talk to remotes: manage them, fetch, pull and push with real progress and honest error reporting —
and stash, so switching context never costs work.

## Context & constraints

- **`pull` always passes `--no-rebase`.** The command factory refuses `rebase` outright, so a
  rebase-configured repository (`pull.rebase=true`) cannot silently rebase behind the user's back.
- Credentials are the system's: `GIT_TERMINAL_PROMPT=0` plus the user's configured credential helper
  and SSH agent. When git needs a credential it cannot get, the operation fails with a specific,
  actionable message rather than hanging on an invisible prompt.
- Network operations are cancellable, and cancellation kills the child process tree.

## PHASE01 — Fetch, pull, push & remotes

**Status:** DONE — see `docs/done/FEATURE-06FE-PHASE01.md`

**Steps**

1. `IRemoteService`: `ListAsync`, `AddAsync(name, url)`, `RenameAsync`, `RemoveAsync`,
   `SetUrlAsync(name, fetchUrl, pushUrl)`.
2. `ISyncService`:
   - `FetchAsync(remote?, prune, fetchTags, ct)` — `--all` when no remote is named;
   - `PullAsync(remote, branch, ct)` — always `--no-rebase`, with `--ff-only` offered as a setting;
   - `PushAsync(PushRequest, ct)` — upstream tracking on first push (`-u`), tag pushing,
     `--force-with-lease` only (never bare `--force`), and delete-remote-branch;
   - progress parsed from `--progress` stderr (`Counting/Compressing/Receiving/Resolving objects`,
     percentages) into `IProgress<SyncProgress>`.
3. Error mapping: authentication failure, host-key mismatch, non-fast-forward rejection, no upstream
   configured, and network unreachable each map to a distinct `SyncFailure` with an actionable
   message; everything else passes git's stderr through verbatim.
4. UI: a sync toolbar (Fetch / Pull / Push with ahead-behind counters), a remotes page (list, add,
   edit, remove with confirmation), and progress in the `Overlay` with a working Cancel.
5. A pull that stops on conflicts hands off to FEATURE-6DCC's conflict state rather than reporting a
   generic failure.

**Acceptance criteria**

- Integration tests against a **local** "remote" repository (a second directory — no network needed):
  add/rename/remove a remote; fetch brings new commits; fetch `--prune` removes a deleted remote
  branch; push publishes a new branch and sets upstream; a non-fast-forward push is rejected and maps
  to the right `SyncFailure`; `--force-with-lease` succeeds when the lease holds and fails when it
  does not; pull fast-forwards; pull creating a merge commit works; deleting a remote branch works.
- Unit tests: progress parsing over captured stderr transcripts; error mapping over captured failure
  transcripts; `PullAsync` always contains `--no-rebase` and never `--rebase`.
- No git invocation anywhere in the solution contains the string `rebase` as a verb (a test scans the
  command factory's surface).

## PHASE02 — Stash management

**Status:** TODO

**Steps**

1. `IStashService`: `ListAsync` (`git stash list` with a format template), `PushAsync(message,
   includeUntracked, keepIndex, paths?)`, `ApplyAsync(index)`, `PopAsync(index)`, `DropAsync(index)`,
   `ShowAsync(index)` returning a patch for the diff viewer, and `BranchAsync(index, name)`.
2. UI: a stash list in the side panel with message, date and file count; apply/pop/drop with
   confirmation on drop; the diff viewer shows a stash's contents; "Stash all changes" in the changes
   page toolbar.
3. `refs/stash` appears in the graph's ref decorations.

**Acceptance criteria**

- Integration tests: push a stash with and without untracked files; list reports the right count,
  messages and order; apply keeps the entry, pop removes it; drop removes the right index; show
  returns a parseable patch; a stash that conflicts on pop leaves the conflict state visible;
  branching from a stash creates the branch with the changes applied.
- Unit tests: `git stash list` format parsing, including messages containing the separator characters.

## Out of scope

- Rebase (forbidden), cherry-pick and revert (recorded as follow-ups — they are safe and welcome, but
  out of this run's scope).
- Submodule update workflows (follow-up).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Pull strategy | Merge, always `--no-rebase`; `--ff-only` available as a setting | Rebase is forbidden; passing the flag explicitly defends against a repository configured with `pull.rebase=true` | honouring the repository's `pull.rebase` (would rebase); `--ff-only` by default (fails constantly on shared branches) |
| Force push | `--force-with-lease` only | It refuses to clobber work the user has not seen; a bare `--force` has no upside in a GUI | offering `--force`; forbidding force entirely (blocks a legitimate amend-and-push) |
| Credentials | The user's own credential helper / SSH agent, prompts disabled | A GUI must never hang on an invisible terminal prompt, and reusing the system helper means no secret of ours to leak | our own credential store for git itself; enabling terminal prompts |
| Remote test fixture | A second local directory as the remote | Gives real push/fetch/prune/lease semantics with no network and no flakiness | mocking git (proves nothing); hitting a real host (flaky, needs secrets) |
