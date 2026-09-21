# FEATURE-0667-PHASE01 — What a local branch knows of its remote

**Item:** FEATURE-0667 — Branch rows say where they stand
**Branch:** `feature/feature-0667-phase01-tracking`
**Run:** feature/2026-09-21-icons-tracking-dragging

## Summary

A branch row can now answer the two questions the page could not: *is this branch on a remote*, and
*how far is it from it*.

The distance was already there — `for-each-ref` reports `%(upstream:track)` and `RefParser` turns it
into `BranchTracking` — so nothing new is run against git. What needed deciding is the other question,
because **being on a remote is not the same as tracking one**. A branch pushed with a plain
`git push origin main` has no upstream: git reports no upstream and no ahead/behind for it, and it is
on origin all the same. Reading the upstream alone would have called it local-only every time it was
looked at. `BranchesPageViewModel.PublishedBranches` therefore answers from both — the upstream when
the branch names one and that ref still exists, the remote-tracking ref of the same name otherwise —
once per rebuild, from the **whole** ref collection, because the filter box hides branches rather than
unpublishing them.

The row states the three outcomes separately, so the view never has to work one out from the others:
`IsPublished` (and the ref it is published as), `IsLocalOnly`, and `IsUpstreamGone` — which is neither
of the first two: a branch pointing at something that is not there any more. `IsAhead` and `IsBehind`
are now gated on the row being a local one; remote-tracking rows have carried two always-empty
counters since the page was written.

## Files / modules touched

**Modified — App**

- `ViewModels/Pages/BranchesPageViewModel.cs` — `BranchRowViewModel` takes the remote-tracking branch
  it is published as and exposes `IsLocal`, `PublishedAs`, `IsPublished`, `IsLocalOnly`,
  `IsUpstreamGone` (local-only now), the tooltips `AheadTip` / `BehindTip` / `RemoteStateTip`, and
  gates `IsAhead` / `IsBehind` on being local; `BranchesPageViewModel.PublishedBranches` resolves the
  lookup once per rebuild

**Modified — tests**

- `tests/Enigma.GitClient.App.UnitTests/BranchesPageTests.cs` — `BuildTrackingWorldAsync`, a real
  repository with a real remote and a second clone: a branch tracking its upstream that has drifted
  one commit each way, a branch on the remote without tracking it, two branches on no remote, and one
  whose upstream was deleted under it. Six tests over it, plus a `GitInAsync` helper so a second
  working tree can be driven

**Modified — docs**

- `docs/roadmap.md`, `docs/plan/FEATURE-0667.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Where the remote branch a row is on comes from | A constructor argument with a default of none | It is a fact about the repository, not about the row; the page resolves it once and hands it over, and the default keeps every existing construction site — including the tests' — compiling and meaning what it did |
| Whether a configured upstream is trusted on its own | No: the ref has to exist among the remote branches | `[gone]` covers the deleted case, but an upstream pointing at a remote nobody fetches any more would otherwise read as published. Falling back to the name match is both safer and more often right |
| Which remote wins when several carry the same branch name | The first one read | The collection is ordered, so the answer does not shuffle between refreshes; naming every remote a branch happens to exist on is a different feature |
| How the tooltips count | "1 commit to push", "2 commits to pull" | A tooltip is a sentence someone reads; "1 commits" is the kind of thing that makes a UI look unfinished |
| Whether `IsUpstreamGone` stays true for a remote row | No, it is gated on `IsLocal` too | Every one of these states is about a local branch. Gating them all in one place is what lets the view ask a row plainly instead of writing the condition again per badge |

## Deviations & follow-ups

- **None from the plan.** All five acceptance criteria are met; the view itself is PHASE02.
- **A flaky test, not this dev's:** `Enigma.GitClient.Core.IntegrationTests.Sync.SyncServiceTests.FetchAsync_ReportsItsProgress`
  failed once and passed on re-run and in every other run of this session. It asserts that git narrated
  a *local-path* fetch on stderr, which git decides for itself when the transfer is tiny; nothing in
  this dev touches the sync service. Recommended follow-up: make that test assert the plumbing rather
  than git's talkativeness. No action taken here.
- **Line endings (recommendation only):** no CRLF churn in the touched files. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1814  failed: 0  succeeded: 1814  skipped: 0
```

Six tests added (1808 → 1814). No fix cycle: nothing in this dev failed at any point.

## Documentation sweep

This dev adds no behaviour a reader can see — the row's new answers reach the screen in PHASE02 — so
nothing in `README.md` or `RELEASENOTES.md` became wrong. There is no `CLAUDE.md`, `CHANGELOG.md` or
`CONTRIBUTING.md` in the repository. No edits.
