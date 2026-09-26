# FEATURE-70C1-PHASE01 — Leaving refs out of the walk

**Item:** FEATURE-70C1 — Hide branches from the history
**Branch:** `feature/feature-70c1-phase01-excluded-refs`
**Run:** feature/2026-09-26-release-1-0-0

## Summary

A commit-log query can now leave named refs out of an every-ref walk: `CommitLogQuery.ExcludedRefs`
holds full ref names (`refs/heads/topic`, `refs/remotes/origin/topic`), and `CommitLogReader` hands each
one to git as `--exclude=<ref>` immediately before `--all` — git applies an `--exclude` to the next
`--all` only. The count query (`rev-list --count`) is built by the same method, so it leaves out the
same refs.

git decides what disappears, and the tests pin its rules against a real repository: a branch's own
commits go; a commit it shares with a visible branch stays (a merged topic's commit is still reached
through the merge); HEAD is walked whatever the list says, so excluding the checked-out branch changes
nothing.

Only an entry that is one full ref name reaches git: it must start with `refs/` and carry no glob
character or whitespace — git refuses `*`, `?` and `[` in ref names, so a well-formed entry can only
ever match itself, and a hand-edited `refs/heads/*` cannot hide every branch. Other scopes ignore the
list. `IsFiltered` counts it.

## Files / modules touched

**Modified — Core**

- `History/CommitLogQuery.cs` — `ExcludedRefs`, and in `IsFiltered`
- `History/CommitLogReader.cs` — `BuildArguments` public (for the argument tests, as the other readers'
  builders are) and the `--exclude` arguments; `IsExcludable`

**Tests**

- `Core.UnitTests/History/CommitLogArgumentsTests.cs` (new) — the excludes sit immediately before
  `--all`; none without a list; nine malformed entries left out while a good one is kept; `Head` and
  `Revision` ignore the list; the count excludes the same refs
- `Core.UnitTests/History/CommitLogQueryTests.cs` — empty by default; counted by `IsFiltered`
- `Core.IntegrationTests/History/CommitLogReaderTests.cs` — a branch's own commit disappears; a shared
  one stays; the checked-out branch cannot be excluded; the count agrees with the page

## Deviations & follow-ups

- None from the plan.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx` — 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx` — 2172 passed, 0 failed (19 new).

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Whitespace in an entry | Refused with the glob characters | git refuses spaces in ref names too; such an entry can only come from a hand edit |
| `BuildArguments`' visibility | `public static`, documented | The house pattern for argument builders (`DiffService`, `MergeService`, `GitIdentityService`) |
