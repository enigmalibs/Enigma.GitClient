# BUG-E74A — Stale Desktop tests red on develop

**Item:** BUG-E74A — Stale Desktop tests red on develop
**Branch:** `bugfix/bug-e74a-stale-desktop-tests`
**Run:** bugfix/2026-10-23-crash-net-token-reset

## Summary

The whole suite on `develop` (41dc222) had two red Desktop tests on Linux, failing alone as in the
whole run. Both were stale expectations left by features built on a Windows machine, where the Desktop
suite was compiled but not run. The product behaviour is the intended one in both cases; only the tests
changed.

- `RepositoryListStoreTests.TwoStoresOverTheSameDirectory_SeeEachOthersWrites` expected `["one", "two"]`.
  FEATURE-1DC2 makes a new repository join the **top** of the list (`entries.Insert(0, entry)`), and its
  completion doc says the order-dependent expectations were updated. This one was missed. It now expects
  `["two", "one"]`, with a comment naming FEATURE-1DC2. It still proves what its name says: each store
  sees the other's write.
- `BranchesPageTests.CollapseAll_IsRememberedAcrossARefresh_AndKeepsTheSelectedBranches` (FEATURE-5595)
  set `SelectedItem` and expected `SelectedBranches` to hold the row. `SelectedBranches` reads the tree's
  own selection (`SelectedNodes`), which only the view, or `SelectBranches` acting as the tree, fills.
  The sibling tests use one or the other. The test now selects through `SelectBranches`, and every
  assertion is unchanged.

## Files / modules touched

**Modified**

- `tests/Enigma.GitClient.Desktop.UnitTests/RepositoryListStoreTests.cs`: one expectation and a comment.
- `tests/Enigma.GitClient.Desktop.UnitTests/BranchesPageTests.cs`: the selection is made the way the
  tree makes it, with a comment.
- `docs/roadmap.md`, `docs/plan/BUG-E74A.md`: statuses.

**Created**

- `docs/done/BUG-E74A.md`

## Decisions taken at build time

| Question | Chosen | Why |
|---|---|---|
| Test or product, list order | Test | FEATURE-1DC2 changed the order on purpose and documents it |
| Test or product, branch selection | Test, using `SelectBranches` | The selection model is the tree's. `SelectedItem` alone is the focus line, never the selection. `SelectBranches` is how the sibling tests act as the tree |

## Deviations & follow-ups

- **No deviation from the plan.**
- **Follow-up:** a Desktop change built on Windows should be run on Linux before it is merged (the
  BUG-6EAA standing instruction is the cause of both stale tests).

## Build/test evidence

- `dotnet build Enigma.GitClient.slnx`: 0 warnings, 0 errors.
- `dotnet test --solution Enigma.GitClient.slnx`: 3343 total, 3340 passed, 3 skipped, 0 failed
  (Core unit, Core integration, Desktop unit). Before the change: the same run had these two failures.
