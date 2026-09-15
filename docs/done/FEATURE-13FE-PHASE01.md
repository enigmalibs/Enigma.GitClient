# FEATURE-13FE-PHASE01 — Status, staging & commit engine

**Item:** FEATURE-13FE — Working directory & commits
**Branch:** `feature/feature-13fe-phase01-status-staging`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

The engine behind the other half of a git client: reading what the working tree and the index look
like, moving changes between them, throwing changes away, and recording commits. No UI yet — this is
the layer PHASE02's changes page sits on.

`git status --porcelain=v2 -z` is parsed in full: the branch headers, ordinary changes, renames and
copies with their original path, every unmerged state, untracked and ignored files, and a
submodule's own dirt. A file that is staged *and* edited again appears in both halves, because it
genuinely is in both, and a client that shows only one of them eventually commits something nobody
looked at.

## Files / modules touched

**Created — Core**

- `Status/WorkingTreeStatus.cs` — `SubmoduleState`, `WorkingTreeBranch`, `WorkingTreeStatus`
- `Status/PorcelainV2Parser.cs` — the whole `--porcelain=v2 -z` grammar
- `Status/StatusService.cs` — `IStatusService`, and `IGitIgnoreService` with `IgnoreRule`
- `Staging/StagingService.cs` — `IStagingService`: stage, unstage, discard, remove
- `Commits/CommitService.cs` — `CommitRequest`, `ICommitService`

**Modified**

- `src/Enigma.GitClient.Core/Files/ChangedFile.cs` — `IndexStatus`, `WorkTreeStatus`,
  `ConflictState`, `IsUntracked` and the submodule state
- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs`
- `docs/roadmap.md`, `docs/plan/FEATURE-13FE.md`

**Created — tests**

- `tests/Enigma.GitClient.Core.UnitTests/Status/PorcelainV2ParserTests.cs` — 41 cases over payloads
  shaped exactly as git writes them, including all seven conflict states, a submodule, a path with
  spaces and a non-ASCII path
- `tests/Enigma.GitClient.Core.IntegrationTests/Status/WorkingDirectoryServiceTests.cs` — 36 cases
  against a real repository: staging, discarding, committing, amending and the ignore rules

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Unstaging | `git restore --staged`, not `git reset` | It says what it does and cannot touch the work tree. `reset` with one wrong argument is how a client loses uncommitted work |
| Discarding a tracked file | `restore --staged --worktree --source=HEAD` | "Discard this file" means it goes back to HEAD, not part-way back to a staged version the user has already forgotten about |
| Discarding an untracked file | Delete it | There is nothing in git to restore it from, so anything else would be pretending |
| An empty selection | Does nothing | An empty pathspec means "everything" to several git commands, which is the exact opposite of what an empty selection means here |
| Where conflicts live | Their own list, never in `Staged` | A conflict is something to resolve, not something to commit, and letting one sit in the staged list is how it gets committed half-resolved |
| Ignored files and "clean" | They do not count | They are on disk on purpose; a tree full of build output is still a clean one |
| Reading an ignore rule | `check-ignore --verbose`, **without** `-z` | git refuses `-z` outside `--stdin` — found by running it. Only the fields before the tab are read, and those are never quoted |
| Parsing that line | A regex with a greedy source | A Windows source path carries a colon, so anchoring on the last colon-digits-colon is what finds the line number instead of the drive letter |
| The commit message | A temporary file with `--file` | An argument vector has a length limit on both platforms and a message legitimately runs to pages; a file also keeps the bytes exactly as typed |
| That file's encoding | UTF-8 with no byte-order mark | A BOM would become the first characters of the subject line |
| Committing with conflicts | Refused before git runs | git would let a half-resolved merge through; the message says which state the repository is in |

## Deviations & follow-ups

- **Deviation:** `IStagingService` has no hunk- or line-level staging. The plan records it as a
  follow-up, and it needs a patch editor, which is its own feature.
- **Deviation:** `IGitIgnoreService.AddPatternAsync` always writes the repository's root
  `.gitignore`. Choosing between a nested one, `.git/info/exclude` and the global file is a question
  the UI has to ask, and there is no UI yet.
- **Follow-up:** nothing watches the filesystem; the status is read on demand. The plan already
  records a watcher as a follow-up — it needs debouncing and ignore-awareness to be worth having.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx --no-incremental
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 1011  failed: 0  succeeded: 1011  skipped: 0
```

77 tests are new in this dev. The parser passed on its first run. Two integration failures were real
findings: `check-ignore` refuses the NUL-separated output unless it is reading from standard input,
and `git log --format=%B` appends a newline of its own that is formatting rather than message.

A third problem was not in the code at all. Writing a unicode escape for the NUL character into a
source file produced a **real control byte** rather than the escape text, which silently turned two
files binary and made `grep` skip them entirely — which is how it was noticed, by a search that
should have matched returning nothing. The whole tree was scanned for control bytes and all three
occurrences repaired. The parser's own tests build their separator from a character code instead, so
that test file can never hold one.
