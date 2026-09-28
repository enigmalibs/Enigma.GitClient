# BUG-28E4 — Untracked file diff is empty

**Item:** BUG-28E4 — Untracked file diff is empty
**Branch:** `bugfix/bug-28e4-untracked-file-diff`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Summary

On the Changes page, selecting a new file that is not staged yet said "This change touches no lines of
text." The page diffs the unstaged side with `git diff -- <path>`, which prints nothing for a file git
has never been told about, so there was no patch to draw.

`DiffService.GetPatchAsync(…, ChangedFile file, …)` now compares an **untracked** file (a working-tree or
uncommitted target) with nothing: `git diff --no-index --patch [whitespace options] --unified=N --
/dev/null <path>`.

- Exit code 1 means "they differ" and is accepted. Anything else still throws.
- The output is an ordinary "new file" patch, parsed like any other. The viewer draws it the way it draws
  a commit's added file: an empty left side, every line green on the right. The context, whitespace and
  "show anyway" options apply as usual.
- A binary untracked file is reported as binary.
- Tracked, staged and committed files are untouched.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Diff/DiffService.cs` — the untracked branch in `GetPatchAsync`,
  `GetUntrackedPatchAsync`, `BuildUntrackedArguments`.
- `tests/Enigma.GitClient.Core.IntegrationTests/Diff/DiffServiceTests.cs` — 4 tests over real git:
  - every line added, for the working-tree and the uncommitted targets;
  - binary;
  - a staged new file unchanged.
- `tests/Enigma.GitClient.Core.UnitTests/Diff/NameStatusParserTests.cs` — the command line.
- `tests/Enigma.GitClient.App.UnitTests/ChangesPageTests.cs` — the Changes page shows the file side by
  side, left empty and right added.
- `docs/roadmap.md`, `docs/plan/BUG-28E4.md` — statuses.

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Rename and copy detection | Not passed | Between one file and none there is nothing to pair |
| Where the branch sits | The `ChangedFile` overload only | It is the only one that knows a file is untracked. The path-only overload stays a plain diff. |

## Deviations & follow-ups

- None from the plan.
- Follow-up (out of scope): the history's uncommitted line lists its files with `git diff HEAD`, which
  leaves untracked files out altogether.
- Line endings: no CRLF churn.

## Documentation sweep

The README's "Working directory: status, stage/unstage, discard and commit" is still accurate. No edit.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: **2451 passed**, 0 failed (6 new), with no fix cycle.
