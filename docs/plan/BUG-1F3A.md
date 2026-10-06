# BUG-1F3A — A stash hides its untracked files

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-1f3a-stash-untracked-files`
**Run:** vibe/2026-10-06-stash-untracked-release

## Objective

Selecting a stash line in the history lists **every** file the stash holds in the details panel —
the untracked files it took as well as the tracked changes — and each of them opens its diff.

## Context & constraints

- Reported on a real repository: one modified tracked file (`src/GlobalAssemblyInfo.cs`) and two
  untracked files (`docs/…`) were stashed. The stash line listed only the tracked file. A pop
  brought all three back, so nothing was lost; the panel did not show the untracked ones.
- The app stashes with `git stash push --include-untracked` (`StashService.PushAsync`). git records
  such a stash as a merge commit `W` with up to three parents:
  - `W^1`: the commit the stash was made on. `W^1..W` is every tracked change, staged or not.
  - `W^2`: the index.
  - `W^3`: present only when untracked files were taken. It is a **root commit** whose tree holds
    exactly those files.
- `HistoryPageViewModel.LoadChangedFilesAsync` reads every line's files with
  `DiffTarget.Commit(row.Sha)`, which runs `git show -m --first-parent`. That is `W^1..W`, which
  never reaches `W^3`.
- The history folds a stash into one line: the row's `GitCommit.ParentShas` keeps only `W^1`, so
  the third parent must be found from git, not from the row.
- The app supports git ≥ 2.20 (`GitVersion.Minimum`). `git stash show --include-untracked` needs
  2.32, so it is not used.
- The diff viewer reads one file's patch with `IDiffService.GetPatchAsync(target, ChangedFile)`.
  The target the panel holds has to serve both halves.

## Steps

1. `DiffTarget`: a new `DiffTargetKind.Stash` and a `DiffTarget.Stash(string sha)` factory.
   `ToString` names it.
2. `DiffService.BuildArguments`: for `Stash`, `diff <sha>^1 <sha>`, which is the tracked half.
3. `DiffService.GetChangedFilesAsync` for `Stash`:
   - list the tracked half;
   - if `git rev-parse --verify --quiet <sha>^3` resolves, list `<sha>^3` as a root commit (every
     file added), and flag each entry `IsUntracked`;
   - merge the two halves in ordinal path order; on a path in both, keep the tracked entry.
4. `DiffService.GetPatchAsync(target, ChangedFile)` for `Stash`: an `IsUntracked` file is read from
   `<sha>^3`; any other file from the tracked half.
5. `HistoryPageViewModel.LoadChangedFilesAsync`: a stash line (`row.IsStash`) uses
   `DiffTarget.Stash`; every other line keeps `DiffTarget.Commit`.
6. Tests:
   - Core integration (`DiffServiceTests`):
     - a stash with tracked and untracked files lists both, with the untracked ones added and
       flagged;
     - a stash without untracked files lists the tracked change only;
     - an untracked-only stash lists its files;
     - the patch of an untracked stash file is every line added;
     - the patch of a tracked stash file is its edit;
     - a path in both halves is listed once.
   - Desktop (`HistoryStashTests`): selecting the stash line fills the panel with all of its
     files. Compiled, **not run** (see *Out of scope*).

## Acceptance criteria

- A stash made with tracked and untracked changes lists all of them on its line's panel.
- An untracked file of the stash opens a diff with every line added.
- A stash without untracked files, and every ordinary commit, lists exactly what it did before.
- Build clean with zero warnings; the Core suites are green.

## Out of scope

- Running the Desktop unit-test suite. It does not finish on Windows; you asked to ignore it this
  session.
- `StashService.ShowAsync`, which needs git ≥ 2.32. It is not used by the panel and is left as is.
- Showing the index (`W^2`) as its own section.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| Where to fix | A `Stash` diff target in Core's `DiffService` | One target serves the list and the viewer; real-git tests in Core; works on git 2.20 | `git stash show --include-untracked` (git ≥ 2.32, no per-file pathspec); merging a second list in the view model (two targets for one panel) |
| How untracked stash files look | As added files (every line added), flagged `IsUntracked` | It is what a pop brings back, and what GitKraken shows | A separate "untracked" marker in the UI (new UI nobody asked for) |
| Finding the third parent | `git rev-parse --verify --quiet <sha>^3` | The folded row no longer carries the stash's other parents; quiet verify answers "absent" without an error | Passing the parents through the row (wider change across the history) |
| A path in both halves (`git rm --cached`, then stash) | The tracked entry is kept | The panel selects by path, so a path can only appear once; the case is rare | Listing both; the untracked entry winning |
| Order of the merged list | Ordinal path order | It is git's own order for a single listing | Tracked first, then untracked |
| Desktop test | Written and compiled, not run | You asked to ignore the Desktop suite this session; the logic under test is covered in Core | No Desktop test |
