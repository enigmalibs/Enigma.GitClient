# BUG-1F3A — A stash hides its untracked files

**Item:** BUG-1F3A — A stash hides its untracked files
**Branch:** `bugfix/bug-1f3a-stash-untracked-files`
**Run:** vibe/2026-10-06-stash-untracked-release

## Summary

Selecting a stash line now lists every file the stash holds, including the untracked files it took.
Each file opens its diff; an untracked one shows every line added.

**The cause.**
- The app stashes with `git stash push --include-untracked`.
- git records the entry as a merge commit `W`:
  - `W^1..W` holds every tracked change;
  - the untracked files go into a **third parent**, `W^3`, a root commit whose tree holds only them.
- The details panel read every line as a commit against its first parent
  (`git show -m --first-parent W`), which never reaches `W^3`.
- So in repository 1170, `src/GlobalAssemblyInfo.cs` (tracked, modified) was listed, and the two
  untracked `docs/` files were not. A pop applies `W^3` too, which is why all three came back.
  Nothing was ever lost.

**The fix.**
- `DiffTarget.Stash(sha)`, a new `DiffTargetKind.Stash`, reads both halves of an entry:
  - the tracked half: `git diff W^1 W`;
  - the untracked half: `W^3` read as a commit, every file in it an addition. It is read only when
    `git rev-parse --verify --quiet W^3` resolves, because most stashes have no third parent.
- `DiffService.GetChangedFilesAsync` merges the two halves:
  - in path order;
  - the untracked files flagged `IsUntracked`;
  - a path in both halves listed once, as its tracked change (`MergeStashHalves`).
- `DiffService.GetPatchAsync`:
  - the `ChangedFile` overload reads an untracked stash file from `W^3`;
  - the path overload falls back to `W^3` when the tracked half does not know the path.
- `HistoryPageViewModel.LoadChangedFilesAsync` uses the stash target for a stash line. Every other
  line keeps `DiffTarget.Commit`, so commits list exactly what they did before.
- It works on git 2.20, the app's minimum. `git stash show --include-untracked` would need 2.32.

## Files / modules touched

**Modified**

- `src/Enigma.GitClient.Core/Diff/DiffTarget.cs`: `DiffTargetKind.Stash`, `DiffTarget.Stash`, its
  `ToString`.
- `src/Enigma.GitClient.Core/Diff/DiffService.cs`:
  - the stash listing (`ListAsync`, `MergeStashHalves`);
  - the third-parent check (`HasUntrackedHalfAsync`, `UntrackedHalf`);
  - patch routing in both `GetPatchAsync` overloads;
  - the `Stash` case of `BuildArguments`;
  - a null check on `target` in the `ChangedFile` overload, which now reads `target.Kind` first.
- `src/Enigma.GitClient.Desktop/ViewModels/Pages/HistoryPageViewModel.cs`: a stash line uses
  `DiffTarget.Stash`.
- `tests/Enigma.GitClient.Core.UnitTests/Diff/NameStatusParserTests.cs` (`DiffTargetTests`):
  - the stash target, and a blank SHA refused;
  - the arguments of the tracked half;
  - the merge order and flags, and a path kept once.
- `tests/Enigma.GitClient.Core.IntegrationTests/Diff/DiffServiceTests.cs`, against real git:
  - a stash lists its untracked files too, while the same commit read as a commit does not;
  - a stash with no untracked file lists its tracked change;
  - a stash of untracked files only lists them;
  - a path in both halves (`rm --cached`, then edited) is listed once, as its tracked change;
  - the patch of an untracked stash file is every line added;
  - the patch of a tracked stash file is its edit;
  - the path overload finds both halves.
- `docs/roadmap.md`, `docs/plan/BUG-1F3A.md`: statuses.

**Created**

- `docs/done/BUG-1F3A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| The path-only `GetPatchAsync` overload for a stash | Falls back to `W^3` when the tracked half has nothing | Otherwise it would answer "no diff" for a file the listing reports; it costs one `rev-parse`, and only on a miss |
| Null `target` in the `ChangedFile` overload | Checked up front | The method now reads `target.Kind` before delegating; without the check a null target would be a `NullReferenceException` instead of an `ArgumentNullException` |
| A Desktop test for the panel | None | You asked mid-run to stop doing Desktop unit tests. The one written was removed before any commit. The panel change is a one-line target choice; the behaviour behind it is covered in Core |
| `MergeStashHalves` visibility | `public static`, like `BuildArguments` | Unit-testable without git, the convention `DiffService` already follows |
| The "path in both halves" test scenario | `git rm --cached`, then **edit**, then stash | Measured against git: after `rm --cached` alone, `W` still holds the file unchanged, so the tracked half lists nothing and the file is only an untracked addition. Only after an edit does the path appear in both halves (tracked `M`, untracked `A`). That is the case the dedupe exists for |

## Deviations & follow-ups

- **Deviations from the plan:**
  - Step 6's Desktop test (`HistoryStashTests`) was dropped. You asked to stop doing Desktop unit
    tests. No Desktop test was run or added in this dev.
  - The plan's example for a path in both halves (`git rm --cached`, then stash) does not produce one
    in git: the file is only in the untracked half. The test and the remark use `rm --cached` plus
    an edit, which does (see *Decisions taken at build time*).
- **Worth a click:** in a repository with a tracked change and two untracked files, stash from the
  toolbar, select the stash line. All three files should be listed, and an untracked one should open
  with every line added. Repository 1170 reproduces it once its three files are stashed again.
- Follow-up, not done: `StashService.ShowAsync` passes `--include-untracked` to `git stash show`,
  which needs git 2.32. On git 2.20–2.31 it would fail and return an empty patch. Nothing in the app
  calls it today.
- Line endings: the touched files are LF; no CRLF churn.

## Documentation sweep

Nothing to change:
- the README's stash bullet (one line per stash; apply, pop and delete from it) is still true;
- the release notes come with FEATURE-47B3;
- no `CLAUDE.md` or `AGENTS.md` exists.

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx -c Debug --no-incremental`: 0 warnings, 0 errors.
- `Enigma.GitClient.Core.UnitTests`: 1165 total, 1164 passed, 1 skipped, 0 failed.
  `AtomicFileTests.AReaderRacingTheWriter_AlwaysReadsAWholeDocument` was excluded
  (`-method- "*AReaderRacingTheWriter_AlwaysReadsAWholeDocument"`), because it hangs on Windows
  (BUG-6EAA).
- `Enigma.GitClient.Core.IntegrationTests`: 358 total, 356 passed, 2 skipped, 0 failed (after fix cycle 1;
  the first run was 358 total, 1 failed — see below).
- `Enigma.GitClient.Desktop.UnitTests`: **not run, and no test added**, as you asked.
- Fix budget: 1 cycle used. The first integration run failed one new test,
  `GetChangedFilesAsync_ForAStash_ListsAPathInBothHalvesOnce` (expected `Deleted`, got `Added`): its
  scenario put the path in the untracked half only. After reproducing in plain git, the scenario now
  edits the file, and the assertion is unchanged in strength.
