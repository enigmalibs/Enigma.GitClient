# FEATURE-7CFD-PHASE04 — Refs, branches, tags & HEAD state

**Item:** FEATURE-7CFD — Solution foundation & git engine
**Branch:** `feature/feature-7cfd-phase04-refs`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Completed the read side of the engine: the reference model (branches with upstream and ahead/behind,
lightweight and annotated tags, the stash and note refs), a single `for-each-ref` read that returns
all of it at once, the HEAD state including unborn, detached and in-progress operations, the remote
reader, and the commit-to-refs decoration index the graph will draw badges from.

With this phase FEATURE-7CFD is complete: everything the history view needs can now be read out of a
real repository, headlessly and under test.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/Refs/`**

- `GitRef.cs` — the reference base plus `GitRefKind`
- `GitBranch.cs` — local/remote, current, upstream, `BranchTracking` (ahead / behind / gone), tip
  author, date and subject; `RemoteName` and `NameWithoutRemote` for nested branch names
- `GitTag.cs` — lightweight vs annotated, tag object SHA, tagger, message, tagged-commit date; plus
  `GitOtherRef` for the stash and note refs
- `HeadState.cs` — unborn, detached, branch name, SHA, `RepositoryOperation`, `DisplayName`
- `RefParser.cs` — the 17-atom `for-each-ref` template, its parser, and `ParseTracking`
- `RefCollection.cs` — refs split by kind, with `CurrentBranch` and `Stash`
- `RefDecorationIndex.cs` — commit SHA to refs, ordered current branch → local → remote → tag → stash
- `IRefReader.cs` — `IRefReader`, `RepositoryRefState`, `IRemoteReader`
- `RefReader.cs` — `RefReader`, `RefReader.DetectOperation`, and `RemoteReader` with its parser
- `GitRemote.cs` — name, fetch/push URLs, host extraction for URI and scp-shaped URLs

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs` — registers
  `IRefReader` and `IRemoteReader`
- `src/Enigma.GitClient.Core/History/CommitLogParser.cs`,
  `tests/.../History/CommitLogParserTests.cs` — see the cross-phase fix below
- `docs/roadmap.md`, `docs/plan/FEATURE-7CFD.md` — status updates

**Created — tests**

- Unit: `RefParserTests` (22 cases), `RefDecorationIndexTests` (8), `RefModelTests` (14),
  `RemoteReaderParseTests` (6)
- Integration: `RefReaderTests` (17), including a real push/fetch against a bare local remote for
  ahead/behind and `[gone]`, and a real conflicting merge for the in-progress operation

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| One call or many | A single `for-each-ref` with a 17-atom template | Asking git per ref is both slower and racy — refs can move between calls, so a per-ref walk can return a self-inconsistent snapshot |
| `refs/remotes/<remote>/HEAD` | Dropped from the branch list | It is a symbolic pointer at the remote's default branch, not something a user can check out; leaving it in shows every default branch twice |
| `RepositoryOperation.Rebase` | Detected, never started | The client cannot rebase, but another tool can leave a repository mid-rebase. Detecting it lets the UI explain the state instead of misreading it. The rebase directories are checked **first**, because a stopped interactive rebase also leaves a `CHERRY_PICK_HEAD` behind |
| Unborn HEAD keeps its branch name | Yes | `git symbolic-ref HEAD` still answers on a fresh repository, and that name is the branch the first commit will create — worth showing in the title bar |
| Annotated tag target | `%(*objectname)` for the commit, `%(objectname)` kept as `TagObjectSha` | The graph must decorate the commit, not the tag object; the tag object SHA is still needed to read the tag's own metadata |
| `GitTag.TargetDate` | Added | The tags list has to sort lightweight and annotated tags together, and only the dereferenced committer date is comparable across both |
| Remote URL parsing | Tab-delimited name, then the last space before `(fetch)`/`(push)` | A remote URL can legitimately contain spaces (a local path). Splitting on whitespace would truncate it — a test covers exactly that |
| Branch ordering | Current branch first, then case-insensitive alphabetical | It is the order both the branches page and the graph badges want, and doing it once in the reader keeps every consumer consistent |

## Deviations & follow-ups

- **Deviation (additive):** `RepositoryRefState` and `IRefReader.GetStateAsync` were not in the plan.
  Every consumer needs refs, HEAD and the decoration index together and consistently; returning them
  from one call removes a whole class of "the branch moved between two reads" bug.
- **Cross-phase fix:** PHASE03 left **raw control bytes** (NUL and 0x1F) embedded in
  `CommitLogParser.cs` and its test, because the escape sequences in the authoring step were resolved
  before the file was written. The code compiled and behaved correctly, but a source file containing
  a NUL byte is hostile to editors, diffs and any tool that treats it as binary. All three files were
  rewritten with proper `\uXXXX` escapes and the whole tree was scanned to confirm none remain. The
  same care was taken in this phase's `RefParser.cs`.
- **Follow-up:** `%(worktreepath)` would let the branches page show which linked worktree has a branch
  checked out, but it needs git 2.23 and the product's floor is 2.20. Revisit if the floor ever moves.
- **Follow-up:** `RefReader` reads every ref in `refs/`. A repository with tens of thousands of refs
  (a large mirror) would benefit from restricting the pattern per view; not worth the complexity
  until a real repository shows the cost.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 248  failed: 0  succeeded: 248  skipped: 0
```

69 tests are new in this dev (50 unit, 19 integration). One fix cycle was used: the first run had a
single failing assertion in `GetStateAsync_BuildsTheDecorationIndexOverEveryRef`, where the test
expected the `feature` branch on the root commit rather than on its own tip — a wrong expectation in
the test, not a defect in the index. The warning count was read explicitly (0).
