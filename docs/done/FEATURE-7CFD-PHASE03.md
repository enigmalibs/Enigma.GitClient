# FEATURE-7CFD-PHASE03 — Commit log reading & model

**Item:** FEATURE-7CFD — Solution foundation & git engine
**Branch:** `feature/feature-7cfd-phase03-commit-log`
**Run:** feature/2026-09-15-enigma-git-client

## Summary

Added the commit model and the history reader: a separator-based `git log` template that survives
multi-line, non-ASCII commit bodies, a paged query object with the selectors and filters the history
page will need, and a reader that treats a repository with no commits as an empty page rather than an
error.

## Files / modules touched

**Created — `src/Enigma.GitClient.Core/History/`**

- `GitSignature.cs` — name, email, `DateTimeOffset` with the original offset preserved, plus the
  initials the history view draws as an avatar
- `GitCommit.cs` — SHA, 7-character short SHA, parents, author, committer, subject, body, and the
  `IsMerge` / `IsRoot` predicates the graph layout needs
- `CommitLogQuery.cs` — scope (all refs / HEAD / a revision), paging, ordering, first-parent,
  author / message / path / date filters, `IsFiltered`, `NextPage()`
- `CommitLogPage.cs` — commits, skip, `HasMore`
- `CommitLogParser.cs` — the format template and its parser
- `ICommitLogReader.cs`, `CommitLogReader.cs` — paged reads, single-commit reads, counting

**Modified**

- `src/Enigma.GitClient.Core/DependencyInjection/ServiceCollectionExtensions.cs` — registers
  `ICommitLogReader`
- `docs/roadmap.md`, `docs/plan/FEATURE-7CFD.md` — status updates

**Created — tests**

- Unit: `CommitLogParserTests` (17 cases), `GitCommitTests`, `CommitLogQueryTests`
- Integration infrastructure: `HistoryFixture` — a repository with a root, a branch, a real merge, an
  unmerged branch, a lightweight tag and an annotated tag, every commit given an explicit timestamp
- Integration: `CommitLogReaderTests` (23 cases)
- `TemporaryRepository` gained `CommitAllAtAsync` / `CommitFileAtAsync` / `GitWithEnvironmentAsync`,
  and `GitCli` an optional per-invocation environment

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Record separator in the log template | NUL (`%x00`), not the plan's `%x1e` | git refuses to create a commit whose message contains a NUL, so the separator provably cannot collide with message content. A record separator *can* legitimately appear in a commit body — and a test asserts a subject containing one survives |
| Extra field separators in the body | Re-joined rather than rejected | The body is the last field, so anything past the tenth belongs to it. Rejecting the record would lose a commit over a character a user is allowed to type |
| Counting | `git rev-list --count`, not `git log` | `--count` is a rev-list option; counting through `log` would have to format and discard a whole payload. The plan did not specify a counting API — it was added because the history page needs a total to show alongside a filtered view |
| Default ordering | `--date-order` | Keeps dates in order *and* never shows a commit before its child, which is what makes a graph readable. `--topo-order` keeps branches contiguous but scrambles dates, so it is offered as an option rather than the default |
| Search semantics | `--fixed-strings --regexp-ignore-case` | A search box where a typed `.` silently becomes "any character" is a bug report waiting to happen. A test asserts `topic.branch` finds nothing while `topic branch` finds two commits |
| Unborn HEAD | Matched against a list of git's own "no commits yet" messages and returned as `CommitLogPage.Empty` | A freshly created repository is a normal state the welcome flow will hit immediately; an exception there would be noise. Anything not on that list still throws `GitCommandException` |
| `log.showSignature` | `--no-show-signature` passed on every read | A user with that configuration would otherwise get PGP output interleaved into every record. A test sets the config and asserts the reader is unaffected |
| `--` terminator | Always appended before the path filters | Without it a branch named like a file (or the reverse) makes git ambiguous and fail |
| Fixture timestamps | Explicit `GIT_AUTHOR_DATE` / `GIT_COMMITTER_DATE` per commit | git's timestamps have one-second granularity, so commits created in a fast test would otherwise order non-deterministically |

## Deviations & follow-ups

- **Deviation (additive):** `CountAsync` was not in the plan. It is one `rev-list` call and the
  history page needs it to show "N of M" under a filter; adding it later would have meant reopening
  this file.
- **Follow-up:** `GitCommit.Message` reconstructs the message as `subject\n\nbody`, which is
  equivalent for display but not byte-identical to `%B` for an unusual message. The amend flow in
  FEATURE-13FE reads `%B` directly rather than relying on this.
- **Follow-up:** a stash's commits are not in `--all`. Stash entries get their own reader in
  FEATURE-06FE PHASE02, and the graph decorates `refs/stash` from FEATURE-7CFD PHASE04's ref index.
- **Line endings (recommendation only):** no CRLF churn observed. No action taken.

## Build/test evidence

```
dotnet build Enigma.GitClient.slnx
  0 Warning(s)
  0 Error(s)

dotnet test --solution Enigma.GitClient.slnx
  Test run summary: Passed!
  total: 179  failed: 0  succeeded: 179  skipped: 0
```

65 tests are new in this dev (42 unit, 23 integration). Both gates passed on the first run; the
warning count was read explicitly (0).
