# FEATURE-1DC2 — New repositories join at the top

**Status:** TODO
**Type:** FEATURE
**Branch:** `feature/feature-1dc2-new-repos-on-top`
**Run:** feature/2026-10-21-ui-polish-release-650

## Objective

A repository that is opened, created or cloned for the first time joins the top of the selected
profile's list instead of its bottom, so the one just added is the first one seen.

## Context & constraints

- `RepositoryListStore.AddAsync` is the only place a repository joins a list. Every way in goes
  through it:
  - *Open* and the command line, through `RepositoryOpener`;
  - *Create* and *Clone*, through `RepositoriesPageViewModel.AddToListAsync`.
- Today a new entry is appended (`entries.Add`). An entry already listed keeps its place and takes
  the new name.
- The list is the user's order: rows are dragged (`MoveAsync`), and opening a listed repository must
  not reorder it.
- The file format (`repository-lists.json`, version 1) does not change, and existing lists keep their
  order.
- The store's tests are in the Desktop suite: written and compiled, **not run** on this machine.

## Steps

1. `RepositoryListStore.AddAsync`: insert a new entry at index 0. A listed one keeps its place and
   takes the new name, as today.
2. Update the XML docs that say "at the end":
   - `IRepositoryListStore` remarks and `AddAsync`;
   - `RepositoriesPageViewModel.AddToListAsync`.
3. Tests (`RepositoryListStoreTests`):
   - `AddAsync_PutsANewRepositoryAtTheEnd` becomes `AddAsync_PutsANewRepositoryAtTheTop`;
   - `ALongListKeepsEveryEntry` expects the newest first;
   - add a test that a listed repository opened again keeps its place.
   - Check the other Desktop tests that build lists through `AddAsync` (reorder, filter, folder,
     profiles, repositories page) and fix any that rely on the append order.

## Acceptance criteria

- Opening, creating or cloning a repository that is not listed puts it first in the list.
- Opening a repository that is already listed leaves the order unchanged.
- The build is clean with zero warnings. The Core suites are green. The Desktop suite compiles.

## Out of scope

- A "most recently used" sort, or any automatic reordering of listed repositories.
- A setting to choose top or bottom.

## Decisions taken autonomously

| Question | Chosen | Why | Alternatives rejected |
|---|---|---|---|
| What moves to the top | Only a repository new to the list | "Make a new repo appear at the top"; the list is the user's drag order, and reordering on every open would undo it | Move every opened repository to the top (MRU) |
| Where | In `RepositoryListStore.AddAsync` | The single entry point for open, create, clone and the command line | Reordering in each caller |
| File format | Unchanged, version 1 | Only the insertion point changes | A version bump |
