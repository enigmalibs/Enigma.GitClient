# BUG-28E4 — Untracked file diff is empty

**Status:** TODO
**Type:** BUG
**Branch:** `bugfix/bug-28e4-untracked-file-diff`
**Run:** bugfix/2026-09-28-history-stash-menus-release

## Objective

On the Changes page, the diff of a new file that is not staged yet says "This change touches no lines
of text." It must look like a commit's added file: nothing on the left, every line green on the right.

## Context & constraints

- The Changes page shows unstaged files — untracked ones included (`ChangedFile.IsUntracked`, from
  `PorcelainV2Parser`) — with `DiffTarget.WorkingTree()`. `DiffService.BuildArguments` runs
  `git diff --patch … -- <path>`, which prints nothing for a file git does not track, so
  `GetPatchCoreAsync` returns `null` and the viewer falls back to the message.
- A staged new file is already right (`git diff --cached`).
- `git diff --no-index -- /dev/null <path>` produces a real "new file" patch (git treats `/dev/null`
  specially on every platform), and exits **1** when the files differ.

## Steps

1. `DiffService.GetPatchAsync(…, ChangedFile file, …)`: for an untracked file under a working-tree or
   uncommitted target, read `git diff --no-index --patch [options] -- /dev/null <path>`, accepting exit
   code 1, parse it as usual; the rename/copy flags are not passed there.
2. Tests: Core integration — an untracked text file gives a patch whose lines are all added, with the
   file's line count; an untracked binary file is reported binary; App — the Changes page shows the
   added lines side by side with an empty left side.

## Acceptance criteria

- Selecting an untracked file on the Changes page shows all its lines as added, left side empty, as a
  commit's added file does.
- Tracked and staged files are unaffected.
- Build clean with zero warnings; the whole suite green.

## Out of scope

- Untracked files in the history's uncommitted line (its file list comes from `git diff HEAD`).

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| How to diff an untracked file | `git diff --no-index -- /dev/null <path>` | Real git patch: binary detection, the same parser, no index change | `git add --intent-to-add` (writes the index); building the patch in C# (reimplements git) |
